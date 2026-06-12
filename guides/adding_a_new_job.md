# Adding a New Animation Job

How to bring a new set of animations from Omniverse Nucleus into the AR guidance app end-to-end.

---

## Overview

The pipeline has four stages:

```
[Omniverse USD Composer]       [FastAPI Server]              [Unity / Vuzix]
  export_glbs_from_usd.py  →  POST /omni/jobs/{id}/prepare  →  gRPC stream
  (runs in Script Editor)      (pulls GLBs + writes YAML)       (plays GLBs)
```

---

## Step 1 — Discover what USD files exist on Nucleus

Open `PU_Segment_Assembly.usd` (or whatever the new assembly file is) in USD Composer, then run this in the **Script Editor** to see all composed layer paths:

```python
import omni.usd

stage = omni.usd.get_context().get_stage()

print("=== LAYERS ===")
for layer in stage.GetLayerStack():
    print(layer.identifier)
```

The output tells you:
- The **Nucleus folder** where the individual part USD files live (becomes `NUCLEUS_BASE`)
- The **exact USD filenames** for each part (become the `usd_basename` in `PARTS`)

> Each part usually has two layers: a main animation `.usd` and a companion `-Position.usd`.
> Only the main animation file goes into `PARTS` — the position layer is a sublayer and gets
> baked in automatically when the script flattens the stage.

---

## Step 2 — Configure `export_glbs_from_usd.py`

Open `server-kit/app/omniverse/export_glbs_from_usd.py`.

There are two config blocks at the top. Add a new one (or swap the active one) with three values:

### 2a. Job ID, Nucleus paths

```python
JOB_ID = "your-new-job-id"          # used as folder name on Nucleus + key in YAML
NUCLEUS_BASE = "omniverse://141.43.76.21/Projects/DIREKT/<path-to-folder-with-part-USDs>"
NUCLEUS_OUTPUT_ROOT = "omniverse://141.43.76.21/Users/shahan"
```

### 2b. PARTS list — one entry per part USD file

```python
PARTS: list[PartSpec] = [
    PartSpec("step-001", "part_snake_case", "Human Readable Name", "ExactUSDFilename_NoExtension", 1),
    PartSpec("step-002", "part_snake_case", "Human Readable Name", "ExactUSDFilename_NoExtension", 2),
    # ...one line per part, sequence_index matches assembly order
]
```

| Field | What it is |
|-------|-----------|
| `step_id` | Stable ID used in YAML and manifests, e.g. `"step-001"` |
| `part_id` | Snake_case name used for the exported GLB filename |
| `display_name` | Label shown in the AR HUD |
| `usd_basename` | Exact USD filename **without** `.usd` — must match Nucleus exactly (spaces allowed) |
| `sequence_index` | Assembly order, 1-based |

> **Single USD with multiple animations?** Check if the assembly USD actually composes from
> separate per-part USD files (run Step 1 to check LAYERS). If yes, each part USD = one
> `PartSpec` entry pointing to that file. If it truly is one monolithic USD, all entries share
> the same `usd_basename` and you get N identical GLBs — less ideal but works.

---

## Step 3 — Run the export in USD Composer Script Editor

1. Copy the entire `export_glbs_from_usd.py` file
2. Open USD Composer → **Window → Script Editor**
3. Paste and hit **Run (Ctrl+Enter)**

Watch the output panel for:
```
=== Exporting 6 parts for job 'your-new-job-id' ===
[step-001] Opening omniverse://...
[step-001] Wrote omniverse://.../part_snake_case.glb
...
Success: Report written to omniverse://.../your-new-job-id/_export_report.json
```

If any step fails, the error message says which USD file couldn't be opened. Fix the `usd_basename` or `NUCLEUS_BASE` and re-run.

---

## Step 4 — Pull GLBs from Nucleus to the FastAPI server

With the FastAPI server running, call:

```
POST /omni/jobs/{job_id}/prepare
  ?nucleus_export_path=/Users/shahan/{job_id}
  &target_version=2026-03-10.1
  &target_file=Fixture_detectors_1.dat
```

Example for the PU Segment job:
```
POST /omni/jobs/pu-segment-assembly/prepare
  ?nucleus_export_path=/Users/shahan/pu-segment-assembly
  &target_version=2026-03-10.1
  &target_file=Fixture_detectors_1.dat
```

This call:
1. Reads `_export_report.json` from Nucleus
2. Downloads each GLB to `shared/samples/assets/_raw/{job_id}/`
3. Hashes each GLB → creates `shared/samples/assets/sha256_xxx/` versioned folders
4. Writes `shared/samples/manifests/{job_id}.manifest.json`
5. Adds a new block to `shared/samples/step-definitions.yaml` **without touching existing jobs**

---

## Step 5 — Verify

Check these three things:

| What | Where | Expected |
|------|-------|----------|
| Raw GLBs | `shared/samples/assets/_raw/{job_id}/` | One `.glb` per part |
| Manifest | `shared/samples/manifests/{job_id}.manifest.json` | Exists, has all steps |
| YAML | `shared/samples/step-definitions.yaml` | New job block appended, other jobs untouched |

---

## Step 6 — Optional: re-package with Draco compression

If Draco is enabled on the server, run:

```
POST /api/jobs/{job_id}/packages:build
```

This re-packages the GLBs with compression and writes to the export asset root.
Skip this if Draco is disabled — Unity fetches GLBs directly from the `sha256_xxx` folders via `/api/assets/`.

---

## Common Errors

| Error | Cause | Fix |
|-------|-------|-----|
| `open_stage_async returned False` | `usd_basename` or `NUCLEUS_BASE` path is wrong | Run Step 1 again, copy exact layer paths |
| `Task exception was never retrieved` | `PARTS` list is not defined (still commented out) | Uncomment / define `PARTS` before running |
| `Result.ERROR_NOT_FOUND` in listing | Passed a file path to `omni.client.list()` — it needs a folder | Remove the filename from the end of the path |
| `_export_report.json` not found on server | Script Editor run failed silently | Check Script Editor output for per-step errors |
| `demonstrator-26-02-25-img` missing from YAML after prepare | Server was running old code before the regex fix | Restart uvicorn to pick up latest `nucleus_job_service.py` |
