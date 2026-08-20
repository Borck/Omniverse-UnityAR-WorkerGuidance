# Live-Sync Architecture

This document describes the live-sync subsystem of the Omniverse → Unity AR Worker
Guidance pipeline: how a change to a USD file on Nucleus is detected, propagated
through the build pipeline, surfaced to the FastAPI/gRPC server, and (in the planned
delivery phase) reflected on the Vuzix M400 smart glasses.

It pairs with:

- [components.md](components.md) — per-file reference
- [configuration.md](configuration.md) — every YAML key and env var
- [operations.md](operations.md) — setup, running, smoke tests, troubleshooting

## 1. Goal

Give an artist the experience that **saving a USD file in Omniverse produces an
updated 3D step on the worker's AR glasses within ~60 seconds, with no human
intervention**. This is the canonical Digital Twin loop applied to assembly
instructions: the source of truth (USD on Nucleus) and the consumer (Unity on
Vuzix) stay in sync continuously.

## 2. Two halves

The system is split into two largely independent halves connected by a shared
file: `shared/samples/manifests/<job_id>.manifest.json` on AT21's local disk.

```
─────────────────────────────  Omniverse side  ─────────────────────────────
  Artist saves USD on Nucleus
       │
       ▼
  watcher polls Nucleus, debounces
       │
       ▼
  pipeline_runner orchestrates one rebuild
       ├── Kit headless: export_glbs_from_usd.py
       │      writes new GLBs to Nucleus
       └── nucleus_job_service.prepare_job() (in Kit Python)
              downloads, hashes, writes manifest.json + step-definitions.yaml

                       ┌──────────────────────┐
                       │  manifest.json on    │
                       │  AT21 local disk     │   ← contract boundary
                       └──────────────────────┘

─────────────────────────────  Delivery side  ──────────────────────────────
  manifest_watcher (in gRPC server) polls the manifest file
       │
       ▼
  Diffs against last-seen snapshot, finds changed step IDs
       │
       ▼
  session_channels broadcasts ManifestUpdated to every gRPC session
  subscribed to that job_id
       │
       ▼
  Unity (on Vuzix) receives ManifestUpdated → refetches changed GLBs via
  existing StreamStepAsset → hot-swaps the model under the live Vuforia anchor
```

Both halves are running on **AT21** in the current deployment but the design does
not require them to be co-located. Splitting onto separate hosts requires only
network reachability for Nucleus and a shared filesystem location for the
manifest. No code changes.

## 3. The shared contract: the manifest file

The whole system pivots on the JSON manifest at:

```
shared/samples/manifests/<job_id>.manifest.json
```

Each entry is one step of the assembly:

```json
{
  "jobId": "example-job",
  "workflowVersion": "1.0.0",
  "steps": [
    {
      "stepId": "step-001",
      "partId": "Part_A",
      "assetVersion": "sha256_a1b2c3d4e5f60718",
      "glbFile": "part_Part_A_a1b2c3d4.glb",
      "stepJsonFile": "step-001.json",
      "targetVersion": "2026-03-10.1",
      "targetFile": "example_fixture.dat",
      "compression": "NONE"
    }
  ]
}
```

The `assetVersion` is a SHA-256 of the GLB content (first 16 hex digits). When
content changes, the hash changes, the manifest entry changes, the diff in
`manifest_watcher` fires, and `ManifestUpdated` is pushed to clients.

If a USD save produces byte-identical GLBs (e.g., the save did not actually
change the geometry/animation), every hash stays the same and no push happens.
Saves that produce no real change are correctly silent — bandwidth and Unity
work are both zero.

## 4. End-to-end sequence

```
[startup] watcher.py derives the watch list from assembly_definition.json:
          the JSON itself + model_target .dat/.xml + one step_id_<M>_<N>.usd
          per is_animation step. (watch_paths: [] in config = auto.)

[t=0]     Artist saves step_id_1_2.usd on Nucleus

[t=0..5]  watcher.py polls Nucleus every 5s via omni.client.stat().
          Detects modified_time on step_id_1_2.usd has increased,
          accumulates URL in pending_changes set.

[t=5..15] Debounce window. Other saves arriving in this window are coalesced
          into the same pending_changes set.

[t=15]    Debounce expires. watcher.run_pipeline() sets the env var
            LIVESYNC_CHANGED_URLS=omniverse://.../step_id_1_2.usd
          and spawns pipeline_runner.py via Kit's Python (sys.executable).

[t=15..25] pipeline_runner.py runs two stages:

           Stage 1 — Kit GLB export (subprocess, ~7s incremental / ~17s full):
              repo.bat launch --name direkt_export.kit -- --no-window
                --exec export_glbs_from_usd.py
              Kit boots headless, reads LIVESYNC_CHANGED_URLS, exports ONLY
              the parts whose source URL is in the changed set (matched
              tolerantly against both %20 and literal-space forms). Unchanged
              parts are listed in the report with their existing Nucleus
              GLB URL but not re-exported. On completion, os._exit(code) forces
              Kit to terminate so the subprocess returns.

           Stage 2 — nucleus_job_service.prepare_job() (in-process, ~2-3s):
              For every part in the export report, downloads the GLB from
              Nucleus to shared/samples/assets/_raw/<job_id>/. Strips unused
              vertex attributes via glb_slim (UVs + vertex colours, ~-36%)
              BEFORE hashing, so the version reflects the bytes actually
              served. SHA-256 hashes each slimmed file. For any part whose
              hash differs from the last manifest, writes the GLB to a new
              sha256_<hash>/ folder. Rewrites the manifest and
              step-definitions.yaml. Deletes any sha256_<hash>/ folders not
              referenced by the new manifest.

[t=25]    watcher re-derives the watch list (refresh_watch_paths) so steps
          added to / removed from assembly_definition.json are picked up
          without restarting the watcher. New paths have no last_seen entry,
          so the next poll exports them automatically.

[t=45]    Manifest file on disk is now current.

[t=45..47] manifest_watcher (daemon thread inside gRPC server) polls
           shared/samples/manifests/ every 2s. Re-reads the changed manifest,
           diffs assetVersion per stepId, builds:
              ManifestUpdated {
                  job_id: "example-job",
                  new_workflow_version: "1.0.0",
                  changed_steps: { "step-001": "sha256_a1b2c3..." }
              }
           Calls SessionChannels.broadcast_to_job("example-job", msg)
           which puts the message in every connected session's outbound queue.

[t=47..52] Connect() generator in grpc_session_service drains the outbound
           queue at the end of its next iteration (next time the Unity client
           sends a Heartbeat — every 5s). Yields ManifestUpdated to the client.

[t=52..56] Unity receives ManifestUpdated:
              - If step-001 == current step: interrupt the live render,
                request StreamStepAsset for the new asset_version, hot-swap
                the GLB under the live Vuforia ObserverBehaviour.
              - Otherwise: invalidate the cached GLB for step-001 so the
                next navigation to that step fetches the new version.

[t=56]    The wearer sees the updated 3D model overlaid on the physical
          assembly. The whole loop is automatic.
```

Total latency: ~55–60 seconds for a single-part edit, dominated by Kit boot
(~9s) and the debounce window (10s). The Unity hot-swap itself is the
fastest piece of the chain (~5s including GLB transfer).

## 5. Why each piece exists

- **Watcher** lives outside Kit so the loop runs whether or not an interactive
  Kit instance is open. The artist controls Kit independently.
- **Debounce** prevents the cost of a full rebuild firing on every intermediate
  save during active editing.
- **Incremental rebuild** (re-export only the parts whose source URL changed)
  was added when single-part edits showed ~80% of a rebuild's time was wasted
  re-doing unchanged parts.
- **Force-exit (`os._exit`)** of Kit was needed because `omni.kit.app.post_quit()`
  did not reliably terminate `direkt.my_usd_composer.kit`; background services
  kept the event loop alive.
- **Content-hashed asset versions** (rather than incrementing version numbers)
  give automatic deduplication: if a save produces identical bytes, the manifest
  doesn't change, no broadcast happens, no Unity work happens. Self-correcting.
- **SessionChannels** layered alongside (not inside) SessionManager because
  SessionContext is `@dataclass(frozen=True)` and we did not want to disrupt
  the existing session persistence pattern just to add a queue.
- **manifest_watcher polls** at 2s (vs 5s for the Nucleus watcher) because the
  manifest file is on local disk — checking it is essentially free, and lower
  latency feels more responsive.
- **Orphan cleanup** of `sha256_<hash>/` folders runs after every manifest write
  to prevent unbounded disk growth across long-running deployments.

## 6. Current state vs. planned

| Stage | Status |
|---|---|
| 1. Nucleus change detection | ✅ Working (watcher.py) |
| 2. Pipeline orchestration | ✅ Working (pipeline_runner.py) |
| 3. Headless Kit export | ✅ Working with force-exit |
| 4. Incremental rebuild | ✅ Working (env-var filter) |
| 5. Manifest + YAML generation | ✅ Working (nucleus_job_service.py) |
| 6. Orphan cleanup | ✅ Working |
| 7. Proto: `ManifestUpdated` message | ✅ Added to `proto/guidance.proto` |
| 8. FastAPI/gRPC: file watcher + push | ✅ Working (manifest_watcher + session_channels) |
| 9. Unity: receive `ManifestUpdated` | ⏳ Not yet implemented |
| 10. Unity: refetch GLB via `StreamStepAsset` | ⏳ Not yet implemented |
| 11. Unity: hot-swap under live Vuforia anchor | ⏳ Not yet implemented |
| 12. Vuzix M400 demo loop | ⏳ Pending steps 9–11 |

Steps 1–8 form a complete, testable system on the server side. Steps 9–11 are
client-side Unity work, planned next.

## 7. Where to read next

- For *what each file does*, see [components.md](components.md).
- For *every configurable knob*, see [configuration.md](configuration.md).
- For *how to run, verify, and debug*, see [operations.md](operations.md).
