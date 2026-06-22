# Live-Sync Components

One section per file added or significantly modified for the live-sync
subsystem. Each section covers: role, dependencies, interface, and any
non-obvious design choice. For the runtime data flow that ties them together,
see [architecture.md](architecture.md).

---

## Omniverse side

### `tools/packaging/livesync/watcher.py`

**Role:** Long-running polling loop. Watches a configured list of Nucleus URLs;
when one or more change, debounces, then spawns `pipeline_runner.py` with the
list of changed URLs in an environment variable.

**Runtime:** Kit's bundled Python (needs `omni.client`).

**Inputs:**

- `livesync.config.yaml` — paths, poll interval, debounce, log/state file
- Nucleus reachability + cached credentials for the watched USDs

**Outputs:**

- Spawns `pipeline_runner.py` per debounce-completed change burst
- `logs/watcher.log` — INFO-level log of changes and pipeline triggers
- `.livesync_state.json` — last-seen modification times, persisted so a
  restart doesn't fire spurious rebuilds

**Key implementation notes:**

- `pending_changes: set[str]` accumulates across polls within a debounce
  window, so two saves in quick succession produce *one* pipeline run that
  covers both.
- The Nucleus mtime API differs between Kit versions: older builds expose
  `entry.modified_time_ns`, newer ones `entry.modified_time` (datetime or
  float). `stat_nucleus_mtime` tries both.
- The pipeline subprocess uses `sys.executable` (= Kit's Python), *not* the
  project venv — `prepare_job` imports `omni.client` which is Kit-only.

---

### `tools/packaging/livesync/pipeline_runner.py`

**Role:** Orchestrates one complete rebuild cycle. Two stages:

1. Launches Kit headless, runs `export_glbs_from_usd.py`, waits for exit.
2. Imports `nucleus_job_service.prepare_job()` and calls it in-process.

**Runtime:** Kit's bundled Python (Stage 2 imports `omni.client`).

**Inputs:**

- `livesync.config.yaml` — same file the watcher reads
- `LIVESYNC_CHANGED_URLS` env var — passed by the watcher; pipe-separated list

**Outputs:**

- `logs/runs/pipeline-<timestamp>.log` — combined Kit + prepare_job output
- Updated `shared/samples/manifests/<job_id>.manifest.json`
- Updated `shared/samples/step-definitions.yaml`
- Updated `shared/samples/assets/sha256_<hash>/` folders

**Key implementation notes:**

- Stage 1 is a subprocess so a Kit crash or hang can't take down the
  watcher. Stage 2 is in-process because `prepare_job` is fast (~3s),
  pure Python with `omni.client`, and we want failures visible in the
  pipeline log without an extra subprocess layer.
- The Kit command template lives in the config (`kit_export_command`) so
  different Kit App Template versions can be supported by editing one line.
- Backward-compatible: with no `LIVESYNC_CHANGED_URLS`, a full rebuild
  runs (this is what `trigger_now.bat` does for manual testing).

---

### `tools/packaging/livesync/run_watcher.bat`

**Role:** Entrypoint for the watcher. Invokes Kit's bundled Python to run
`watcher.py`. Typically launched once at boot via Windows Task Scheduler.

**Why a .bat?** PowerShell scripts require execution policy changes on some
locked-down Windows installs. `cmd.exe` runs `.bat` files with no policy
adjustment, which makes Task Scheduler entries much simpler to deploy.

---

### `tools/packaging/livesync/trigger_now.bat`

**Role:** Manual rebuild trigger. Runs `pipeline_runner.py` with no
`LIVESYNC_CHANGED_URLS` — i.e., a full rebuild of every part. Bypasses the
watcher entirely. Used for smoke testing on a fresh host before turning on
polling.

---

### `server-kit/app/omniverse/export_glbs_from_usd.py`

**Role:** The Kit-side exporter. Opens each per-part USD on Nucleus,
flattens the composed stage, converts to GLB via `omni.kit.asset_converter`,
writes the GLB and a JSON report back to Nucleus.

**Runtime:** Inside Kit (USD Composer's Script Editor or
`kit.exe --exec` headless).

**Inputs:**

- Hardcoded `JOB_ID`, `NUCLEUS_BASE`, `NUCLEUS_OUTPUT_ROOT` constants
  (per-job; edit when switching between jobs)
- `PARTS: list[PartSpec]` — each entry maps a step_id and part_id to a
  source USD filename
- `LIVESYNC_CHANGED_URLS` env var (optional) — pipe-separated list; if
  set, only parts whose source URL is in the set are re-exported

**Outputs:**

- `omniverse://.../<NUCLEUS_OUTPUT_ROOT>/<JOB_ID>/<part_id>.glb`
- `omniverse://.../<NUCLEUS_OUTPUT_ROOT>/<JOB_ID>/_export_report.json`

**Key implementation notes:**

- Force-exits with `os._exit(exit_code)` after `run()` completes. The
  polite path (`omni.kit.app.get_app().post_quit()`) is unreliable in
  `direkt.my_usd_composer.kit` because background services and renderer
  threads keep Kit's event loop alive indefinitely.
- The exporter writes GLBs to Nucleus, not local disk. `nucleus_job_service`
  then downloads them. This is a deliberate Nucleus round trip that adds
  ~1–2s per part but is robust if the Kit machine and the FastAPI machine
  ever become different hosts.
- Filename URL encoding: the script handles spaces in basenames
  (e.g., `CORES_001 CORES_002`) by URL-encoding to `%20`. The incremental
  matching logic in `_part_was_changed` accepts both encoded and decoded
  forms so the watcher's literal-space URLs from YAML still match.

---

### `server-kit/app/omniverse/nucleus_job_service.py`

**Role:** Closes the loop between Kit's Nucleus-side output and the
FastAPI server's local-disk inputs. Reads the export report from Nucleus,
downloads each GLB locally, hashes for content-addressed storage, writes
the runtime manifest and step-definitions YAML, cleans up orphaned
versioned folders.

**Runtime:** Kit's bundled Python (uses `omni.client`).

**Entrypoint:** `prepare_job(nucleus_export_path, repo_root, target_id,
target_version, target_file) -> {"job_id", "steps_synced", "orphans_removed"}`

**Outputs:**

- `shared/samples/assets/_raw/<job_id>/` — downloaded GLBs (always
  overwritten — no caching, because the filename on Nucleus is stable
  but its contents change on every save)
- `shared/samples/assets/sha256_<hash>/` — content-addressed versioned
  folders, one per unique GLB
- `shared/samples/manifests/<job_id>.manifest.json` — the manifest
  served by FastAPI
- `shared/samples/step-definitions.yaml` — updated in place with a
  regex-based replace of just the matching job entry

**Key implementation notes:**

- Stage 6 (orphan cleanup) walks `shared/samples/assets/` after the
  manifest write and removes every `sha256_*` folder whose name is not
  in the new manifest's `assetVersion` set. Aggressive policy, no
  rollback history, but safe because Nucleus is the source of truth.
- The YAML rewrite uses a regex that matches the *exact* job id (with
  a lookahead for `  - jobId: ` or end-of-string) so prefix-collisions
  like `demonstrator-26-02-25` vs `demonstrator-26-02-25-img` don't
  strip both blocks.
- The `target_id` field is derived from `target_file` if empty:
  `Path(target_file).stem + "_model_target"`. So callers usually pass
  only the filename and let the convention apply.

---

## Delivery side (server-kit/app/)

### `session_channels.py`

**Role:** Thread-safe per-session outbound push channels. Sits alongside
`SessionManager` rather than inside it because `SessionContext` is a
frozen dataclass and adding a queue would have rippled through the
persistence layer.

**Entrypoint:**

```python
channels = SessionChannels()
queue = channels.attach(session_id, job_id)      # Connect() calls this
channels.update_job(session_id, job_id)          # if active job changes mid-stream
channels.detach(session_id)                      # on stream close
channels.broadcast_to_job(job_id, message)       # ManifestWatcher calls this
```

**Threading model:** A single `threading.Lock` guards the two dicts.
External producers (the manifest watcher) call `broadcast_to_job` from
their own thread; the Connect generator drains from its session's queue
on the gRPC worker thread.

---

### `manifest_watcher.py`

**Role:** Polls `shared/samples/manifests/*.manifest.json` every 2 seconds.
Diffs each file's `stepId -> assetVersion` map against the last-seen
snapshot. For any changed steps, builds a `ManifestUpdated` ServerMessage
and broadcasts it to every connected session subscribed to that job.

**Runs as:** Daemon thread started during gRPC server bootstrap. Dies
with the server.

**Inputs:**

- `manifests_dir` — typically `shared/samples/manifests/`
- A `SessionChannels` instance for broadcasting

**Key implementation notes:**

- Seeds `_last_known` at startup from the current state of every manifest
  on disk, so the first poll doesn't broadcast "everything changed."
- `_read_manifest` returns `None` for `JSONDecodeError`/`OSError` — likely
  a manifest mid-write. The next poll will retry.
- If no steps changed AND workflow_version is unchanged, *no* message is
  sent. A no-op save (USD save that produces identical bytes → identical
  hashes → identical manifest) is correctly silent end-to-end.

---

### `grpc_session_service.py` (modified)

**Role:** The bidi gRPC session stream handler. Already existed; the
live-sync change is non-invasive.

**Live-sync additions:**

- New optional constructor parameter `session_channels: SessionChannels | None`.
- After the `hello` handshake completes, calls
  `self._session_channels.attach(session_id, active_job_id)` and keeps the
  returned queue in a local `outbound` variable.
- At the end of every iteration of the `for message in request_iterator`
  loop, drains the queue with `outbound.get_nowait()` and yields each
  pending push as a normal `ServerMessage`. Latency is bounded by one
  client message round-trip (heartbeats fire every ~5s).
- On stream close (loop ends), calls `self._session_channels.detach(session_id)`.

**Why drain-after-iteration rather than a dedicated thread:** the existing
Connect method is a sync generator. Layering an inbound thread would have
restructured a working code path; the drain-after-iteration pattern is
additive and preserves every existing yield site.

---

### `grpc_server_main.py` (modified)

**Role:** Bootstraps the gRPC server. Combined session + asset transfer.

**Live-sync additions:**

- Constructs a `SessionChannels` instance and passes it to
  `GuidanceSessionService`.
- After `server.start()`, instantiates `ManifestWatcher(manifests_dir,
  channels=session_channels, logger=logger, poll_interval_sec=2.0)`
  and calls `.start()`.

---

### `server_kit_main.py` (modified)

**Role:** FastAPI HTTP entrypoint. Independent of the live-sync push path —
the only changes here protect the FastAPI process from failing to start on
hosts where the Omniverse SDK is not installed in the venv.

**Live-sync-adjacent changes:**

- `from app.omniverse.router import router as omniverse_router` is wrapped
  in try/except. On ImportError, the router is set to `None` and a flag is
  recorded; the rest of the app starts normally.
- The FastAPI lifespan that calls `omni.client.initialize()` is similarly
  guarded so AT21's pip venv (which has no `omni.client`) can run FastAPI
  without crashing.
- `/omni/*` HTTP endpoints become unavailable on those hosts, but nothing
  the live-sync delivery path depends on is affected.

---

### `proto/guidance.proto` (modified)

Added one message to the `ServerMessage` oneof:

```proto
message ManifestUpdated {
  string job_id = 1;
  string new_workflow_version = 2;
  // step_id -> new asset_version (sha256_xxx). Only changed steps.
  map<string, string> changed_steps = 3;
}
```

`test-server/Protos/guidance.proto` carries the identical change as a
mirror — they must stay in lockstep. After editing, regenerate Python
stubs with `grpc_tools.protoc` and C# stubs with
`dotnet build` in `tools/proto-csharp/`. See [operations.md](operations.md)
for the exact commands.

---

## Files NOT touched

These exist in the live-sync vicinity but were intentionally left alone:

- `server-kit/app/manifest_service.py` — already correct; just reads
  manifests on demand for `AssetTransferService`. We didn't reuse its
  parsed model in `manifest_watcher` because the watcher only needs
  `stepId -> assetVersion` and avoiding the full parse keeps the poll
  hot path lean.
- `server-kit/app/grpc_asset_service.py` — already handles
  `StreamStepAsset`. The Unity-side hot-swap logic (step 9+) will reuse
  it unchanged to fetch new GLBs.
- `tools/packaging/automate_job.py` and `build_runtime_packages.py` —
  previous-generation manifest builders. Superseded by `prepare_job` for
  the live-sync path but still usable for one-shot manual runs.
