# Live-Sync Configuration Reference

Every configurable value the live-sync subsystem reads, where it lives, and
what it does. Use this as the cheat sheet when porting the system to a new
host or job.

For the running pieces themselves, see [components.md](components.md).
For end-to-end run instructions, see [operations.md](operations.md).

## 1. `tools/packaging/livesync/livesync.config.yaml`

Per-host configuration for the watcher and `pipeline_runner`. Copy
`livesync.config.example.yaml` to `livesync.config.yaml` once per machine.

### General

| Key | Type | Notes |
|---|---|---|
| `repo_root` | string (absolute path) | Where this repository is checked out. All other repo-relative paths resolve from here. |
| `job_id` | string | Must match `JOB_ID` inside `export_glbs_from_usd.py`. Used as the manifest filename stem and the gRPC routing key. |
| `nucleus_export_root` | string (Nucleus path, no host prefix) | Where the Kit exporter drops GLBs and `_export_report.json`. Must match `NUCLEUS_OUTPUT_ROOT` in `export_glbs_from_usd.py` minus the `omniverse://<host>` prefix. |

### Vuforia target

| Key | Type | Notes |
|---|---|---|
| `target_id` | string | Leave empty; `prepare_job` derives it from `target_file` as `<stem>_model_target`. |
| `target_version` | string | Matches the folder name under `shared/samples/targets/`. |
| `target_file` | string | The `.dat` filename. The `.xml` companion is found by replacing the extension. |

### Watching

| Key | Type | Notes |
|---|---|---|
| `watch_paths` | list of strings | Nucleus URLs of the per-part animation USDs. **Only files the artist actually edits to change geometry/animation should be in this list.** Master USDs or layers consumed elsewhere should *not* be included unless they actually flow into the GLBs the exporter produces. |
| `poll_interval_sec` | int | How often `omni.client.stat()` is called for each watched path. Default 5. |
| `debounce_sec` | int | Quiet period after the *last* detected change before the pipeline fires. Default 10. |

### Pipeline behavior

| Key | Type | Notes |
|---|---|---|
| `pipeline_timeout_sec` | int | Maximum allowed runtime for one rebuild before the watcher kills it. Default 600 (10 minutes) — leaves generous headroom for first-time Kit boots that recompile extensions. |
| `kit_app_dir` | string (absolute path) | Where the Kit App Template repo lives. |
| `kit_export_command` | string (template) | The exact command that launches Kit headlessly and runs the exporter. Supports `{repo_root}`, `{kit_app_dir}`, `{job_id}` substitution. |
| `venv_python` | string (absolute path) | The project venv Python. *Not used* by the live-sync pipeline anymore (Kit Python is used instead) but kept for occasional manual `automate_job.py` runs. |

### Logging and state

| Key | Type | Notes |
|---|---|---|
| `log_dir` | string (absolute path) | Where `watcher.log` (rotating) and `runs/pipeline-<timestamp>.log` (per-trigger) live. Created if missing. |
| `state_file` | string (absolute path) | JSON file that persists last-seen mtimes between watcher restarts. Prevents spurious "everything changed" on every cold start. |

### Migrating to a new host

The only fields that should need to change when moving the system to a
different machine are:

- `repo_root`
- `kit_app_dir`
- `venv_python`
- `log_dir`
- `state_file`

The Nucleus URLs and `job_id` are per-job, not per-host, and stay the
same.

---

## 2. `server-kit/app/omniverse/export_glbs_from_usd.py` — module constants

Edited per job. These cannot live in the YAML because the script is
executed by Kit (not by `pipeline_runner.py` directly), so it must be
self-contained for the Kit Python environment.

### Job identifier

```python
JOB_ID = "Segment_Assembly"
```

Must match `job_id` in `livesync.config.yaml`. Becomes the output folder
name on Nucleus, the export report payload, and the manifest filename
on disk.

### Nucleus paths

```python
NUCLEUS_BASE = "omniverse://141.43.76.21/Users/abdul/Animation Chesco"
NUCLEUS_OUTPUT_ROOT = "omniverse://141.43.76.21/Users/abdul"
```

- `NUCLEUS_BASE` is the folder containing the per-part animation USDs.
  Each part's source URL is `f"{NUCLEUS_BASE}/{usd_basename}.usd"`.
  This must be a folder URL, *never* a `.usd` file URL.
- `NUCLEUS_OUTPUT_ROOT` is the folder under which a `<JOB_ID>/`
  subfolder will be created on Nucleus and populated with the
  generated GLBs and `_export_report.json`. Must be a folder the
  Kit-running user has write permission to.

### Parts list

```python
PARTS: list[PartSpec] = [
    PartSpec("step-001", "Plate_Bottom",     "Place bottom plate",       "Plate_Bottom",      1),
    PartSpec("step-002", "Core&Coils",       "Place Cores & Coils",      "Core&Coils",        2),
    ...
]
```

Each `PartSpec` carries:

- `step_id` — stable identifier used in manifests and on the wire
- `part_id` — short filesystem-safe name; becomes the GLB filename
- `display_name` — human-readable label surfaced to the worker
- `usd_basename` — filename (without `.usd`) of the source on Nucleus.
  May contain spaces or special characters; the script URL-encodes
  spaces with `%20`.
- `sequence_index` — assembly order (1-based)

### Converter settings

```python
CONVERTER_SETTINGS = {
    "ignore_materials": True,
    "ignore_animations": False,
    "ignore_camera": True,
    "ignore_light": True,
    "embed_textures": False,
    "smooth_normals": True,
    "use_meter_as_world_unit": True,
    ...
}
```

These produce glTFast-compatible GLBs with animations baked in. The
critical fields are `ignore_animations=False`, `embed_textures=False`,
and `use_meter_as_world_unit=True`. Materials, lights, and cameras are
disabled because the AR client overlays the geometry on a Vuforia anchor
with its own lighting; embedded scene materials cause visual
inconsistency.

---

## 3. `server-kit/app/core/config.py` — server-side constants

```python
SERVER = "omniverse://141.43.76.21"
REPO_ROOT = Path(__file__).resolve().parents[3]
```

- `SERVER` is the Nucleus host prefix that `nucleus_job_service` prepends
  to every path it reads from Nucleus. Must match the host portion of
  `NUCLEUS_BASE` / `NUCLEUS_OUTPUT_ROOT` above.
- `REPO_ROOT` is auto-derived from the script's location and rarely
  needs editing.

---

## 4. `server-kit/app/config.py` — `AppConfig`

Loaded by the gRPC server and FastAPI bootstrap from environment
variables. Most fields are pre-existing; only the manifest path matters
for `ManifestWatcher`:

| Field | Env var | Notes |
|---|---|---|
| `manifests_root` | `APP_MANIFESTS_ROOT` | Directory the manifest watcher polls. Resolves to `shared/samples/manifests/` by default. |
| `asset_root` | `APP_ASSET_ROOT` | Directory whose `sha256_<hash>/` folders FastAPI/gRPC serve assets from. Same path `nucleus_job_service` writes to. |
| `target_root` | `APP_TARGET_ROOT` | Directory containing Vuforia `.dat` / `.xml` files (per `target_version`). |
| `grpc_host`, `grpc_port` | `APP_GRPC_HOST`, `APP_GRPC_PORT` | Default `0.0.0.0:50051`. |
| `step_definition_file` | `APP_STEP_DEFINITION_FILE` | Path to `shared/samples/step-definitions.yaml`. |
| `session_store_file` | `APP_SESSION_STORE_FILE` | JSON file persisting session state across restarts. |

All values are loaded once at startup; changing them requires restarting
the gRPC server and the FastAPI process.

---

## 5. Vuforia target files (local disk only)

The pipeline does **not** touch Vuforia target files. They live at:

```
shared/samples/targets/<target_version>/<target_file>     # the .dat
shared/samples/targets/<target_version>/<target_file_xml> # the .xml (same stem)
```

For example with `target_version: 2026-03-10.1` and `target_file:
Segment_Assembly_Fixture.dat`:

```
shared/samples/targets/2026-03-10.1/Segment_Assembly_Fixture.dat
shared/samples/targets/2026-03-10.1/Segment_Assembly_Fixture.xml
```

These files are produced offline in the Vuforia Target Manager. The
pipeline only records their identifiers in the manifest; FastAPI then
serves them to Unity via `AssetTransferService.StreamStepAsset` with
`asset_type = ASSET_TYPE_VUFORIA_TARGET`.

If the Vuforia files are missing, the manifest will still be built and
the Unity client will still connect, but tracking will fail when the
client requests the target asset.

---

## 6. Vuforia Engine Unity package (developer-provided)

`client-unity/Packages/com.ptc.vuforia.engine-11.4.4.tgz` is a >100 MB
PTC binary, *gitignored* by repo policy. Each developer downloads it
once from <https://developer.vuforia.com/downloads/> and drops it at
that exact path. The version pinned in `client-unity/Packages/manifest.json`
must match the filename exactly.

Without the `.tgz`, Unity will fail to resolve the package and surface
~20 `CS0246: type or namespace 'Vuforia' not found` errors. This is
intentional — see the comment in `.gitignore`.

---

## 7. Environment variables consumed at runtime

| Variable | Set by | Read by | Purpose |
|---|---|---|---|
| `LIVESYNC_CHANGED_URLS` | `watcher.py` | `pipeline_runner.py`, `export_glbs_from_usd.py` | Pipe-separated list of Nucleus URLs that triggered this rebuild. Empty/unset = full rebuild. |
| `OMNI_USER`, `OMNI_PASS` | Set inside `app/omniverse/router.py` (legacy) | `omni.client` Nucleus auth | Embedded credentials. Should ideally move to a config but currently hardcoded for the demo. |
| `APP_*` | OS environment | `AppConfig.from_env` | Standard FastAPI/gRPC service configuration. See section 4. |

---

## 8. Things the pipeline reads but does NOT have a configurable knob for

- The directory layout under `shared/samples/` (`assets/`, `manifests/`,
  `targets/`, `step-definitions.yaml`) is hardcoded across multiple
  components. Reorganizing it requires synchronized changes in
  `nucleus_job_service`, `manifest_service`, `manifest_watcher`,
  `AssetTransferService`, and `step_definition_repository`.
- The `_export_report.json` schema (`job_id`, `parts` list, per-part
  fields like `step_id`, `part_id`, `glb_url`, `ok`, `skipped`) is the
  contract between `export_glbs_from_usd.py` and `prepare_job`. Changing
  it requires touching both files.
- The `sha256_<hash>` versioned folder naming and the
  `part_<part_id>_<first8hex>.glb` filename pattern are hardcoded in
  `prepare_job`. Unity's asset cache assumes the convention. Don't change
  without coordinating Unity-side updates.
