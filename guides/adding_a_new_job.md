# Adding a New Animation Job

How to bring a new set of animations from Omniverse Nucleus into the AR guidance
app end-to-end, using the current config-driven live-sync pipeline.

> **This replaces the old manual workflow.** Earlier versions of this guide told
> you to hardcode a `PARTS` list inside `export_glbs_from_usd.py` and paste the
> script into the USD Composer Script Editor. That is no longer how it works.
> The exporter is now **fully driven by `assembly_definition.json` on Nucleus and
> environment variables from `livesync.config.yaml`** — you do not edit any Python
> to add a job.

---

## Overview

The pipeline has three stages, all automatic once configured:

```
[Nucleus job folder]            [Headless Kit + Server]              [Unity / Vuzix]
  assembly_definition.json  →   pipeline_runner:                 →   gRPC stream
  step_id_<M>_<N>.usd            1. export_glbs_from_usd.py            (plays GLBs)
  model_target/*.dat/.xml           (reads the definition, exports
                                     one GLB per animation step)
                                  2. nucleus_job_service.prepare_job
                                     (downloads, slims, hashes,
                                      writes manifest + step YAML)
```

Nothing about the parts is hardcoded: the step list, order, and which steps are
animated all come from `assembly_definition.json`.

---

## Step 1 — Lay out the job folder on Nucleus

The job lives in one Nucleus folder (the "Animation Export" folder). The pipeline
expects this structure — replace the placeholders with your own values:

```
omniverse://<NUCLEUS_HOST>/<path-to-job-folder>/        ← this is nucleus_job_root
  ├── JSON/
  │     └── <anything>.json          ← the assembly_definition.json (auto-discovered)
  ├── step_id_1_2.usd                ← one animation USD per animated step
  ├── step_id_2_1.usd                   (named step_id_<major>_<minor>.usd, NO suffix)
  ├── ...
  └── model_target/
        ├── <target>.dat             ← Vuforia model target (auto-discovered)
        └── <target>.xml
```

Rules the pipeline relies on:

- **Step USD naming:** a step whose `step_id` is `"1.2"` must have its animation USD
  at `step_id_1_2.usd` in the job-root folder. The dot becomes an underscore and
  there is **no** `_Animation` suffix.
- **Model target:** placed under `model_target/`. The `.dat`/`.xml` are **discovered
  automatically** — you no longer name the target in any config. The target id is
  derived as `<dat-stem>_model_target`.

### The `assembly_definition.json`

This file (authored on the Omniverse side) defines the steps. Its shape:

```json
{
  "operations": [
    {
      "steps": [
        {
          "step_id": "1.2",
          "is_animation": true,
          "instruction": "Place the bottom plate",
          "step_type": "assembly",
          "component_type": "plate"
        },
        {
          "step_id": "1.3",
          "is_animation": false,
          "instruction": "Verify cable clearance before continuing"
        }
      ]
    }
  ]
}
```

| Field | Meaning |
|-------|---------|
| `operations[].steps[]` | All steps, in authoring order → becomes `sequence_index` (1-based) |
| `step_id` | Stable id, e.g. `"1.2"` → source USD `step_id_1_2.usd` |
| `is_animation` | `true` = animated part (a GLB is exported). `false` = instruction-only step (no GLB, text still shown and listed in the manifest) |
| `instruction` | Text shown to the worker (becomes `display_name`) |
| `step_type`, `component_type` | Optional; used only to label the exported GLB file |

Instruction-only steps (`is_animation: false`) are recorded in the export report
with no GLB, so the manifest lists **every** step and the client never fails a
step lookup.

---

## Step 2 — Point `livesync.config.yaml` at the job

This is the **only** file you edit to add a job. Copy the example once per host,
then set the job fields:

```powershell
cd tools\packaging\livesync
copy livesync.config.example.yaml livesync.config.yaml
notepad livesync.config.yaml
```

Set at minimum:

| Key | What to set |
|-----|-------------|
| `job_id` | The new job's identifier — becomes the manifest filename, the Nucleus output subfolder, and the gRPC routing key. Passed to the exporter as `DIREKT_JOB_ID`. |
| `nucleus_job_root` | Full `omniverse://<NUCLEUS_HOST>/<path-to-job-folder>` from Step 1. Everything else (assembly definition, step USDs, model target) is derived from it by convention. |
| `nucleus_export_root` | A writable Nucleus folder where the exported GLBs and `_export_report.json` land (under `<root>/<job_id>/`). |
| `watch_paths` | Leave as **`[]`** so the watch list is auto-derived from `assembly_definition.json` and refreshed after every rebuild. |

Per-host fields (`repo_root`, `kit_app_dir`, `venv_python`, `log_dir`,
`state_file`) are set once per machine, not per job. See
[../docs/live-sync/configuration.md](../docs/live-sync/configuration.md) for every
field.

> The old `target_version` / `target_file` keys are obsolete — the model target is
> discovered from `{nucleus_job_root}/model_target/`.

---

## Step 3 — Run the pipeline

Two ways, same result:

**A. One-shot (recommended when first adding a job):**

```powershell
cd tools\packaging\livesync
.\trigger_now.bat
```

This runs `pipeline_runner` once: headless Kit export → `prepare_job`.

**B. Automatic (ongoing live-sync):**

```powershell
cd tools\packaging\livesync
.\run_watcher.bat
```

The watcher polls Nucleus; when you save the assembly definition or any watched
step USD, it debounces (~10s) and runs the same pipeline. Only the parts whose
source URL changed are re-exported.

Expected tail of a successful run:

```
=== Kit GLB export === ...
[pipeline_runner] Kit GLB export -> exit 0
=== nucleus_job_service.prepare_job === ...
[NucleusJobService] Wrote manifest: ...\<job_id>.manifest.json
[pipeline_runner] prepare_job -> ok (steps_synced=N)
```

---

## Step 4 — Verify

| What | Where | Expected |
|------|-------|----------|
| Manifest | `shared/samples/manifests/<job_id>.manifest.json` | Exists, lists every step |
| Versioned assets | `shared/samples/assets/sha256_*/` | One folder per unique GLB content hash |
| Step definitions | `shared/samples/step-definitions.yaml` | New job block appended, other jobs untouched |

`prepare_job` also deletes any `sha256_<hash>/` folder no longer referenced by the
manifest, so disk usage stays bounded — no manual cleanup.

---

## Step 5 — The client picks it up

For a device to receive this job, its `hello` must advertise it. The Unity client
sends a `capabilities` string containing `job=<job_id>`; without it the session
falls back to the default job. (See the Unity side's `HelloRequest` construction.)

---

## Optional — repackage with Draco

If Draco is enabled on the server:

```
POST /api/jobs/{job_id}/packages:build
```

Repackages the GLBs with mesh compression. Skip if Draco is disabled — Unity
fetches the GLBs directly from the `sha256_*` folders otherwise.

---

## Advanced — manual server-side prepare (no watcher)

If the GLBs and `_export_report.json` already exist on Nucleus (e.g. exported by a
previous run) and you only want the server to pull and index them, the HTTP
endpoint still exists:

```
POST /omni/jobs/{job_id}/prepare?nucleus_export_path=<path-under-host>/<job_id>
```

This runs stage 2 only (download → slim → hash → manifest + YAML). It does **not**
export from USD — use the pipeline (Step 3) for that.

---

## Common errors

| Error | Cause | Fix |
|-------|-------|-----|
| `open_stage_async returned False` / `Could not download ...usd` | A step USD is missing or `nucleus_job_root` points at a file, not a folder | Confirm the `step_id_<M>_<N>.usd` exists in the job root and the path is a folder |
| Kit export times out | Kit didn't self-exit, or first-boot extension compile is slow | Keep the `os._exit` in the exporter tail; raise `pipeline_timeout_sec` in the config |
| `_export_report.json` not found on server | The Kit export failed silently | Read `tools/packaging/livesync/logs/runs/pipeline-<ts>.log` |
| A step missing from the manifest after a run | Server ran stale code, or the step isn't in `assembly_definition.json` | Restart the pipeline; confirm the step exists with the right `is_animation` flag |
| `sessions_notified=0` on update | Client didn't send `job=<job_id>` in `capabilities` | Fix the Unity `HelloRequest` |

For the full runbook (host setup, smoke tests, log locations, stuck-Kit
recovery), see [../docs/live-sync/operations.md](../docs/live-sync/operations.md).
