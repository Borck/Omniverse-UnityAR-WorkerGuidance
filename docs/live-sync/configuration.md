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
| `job_id` | string | The job to rebuild. Manifest filename stem and gRPC routing key. Passed to the exporter as `DIREKT_JOB_ID` — **nothing is hardcoded in the Kit script.** |
| `nucleus_export_root` | string (Nucleus path, no host prefix) | Where the Kit exporter drops GLBs and `_export_report.json`. Passed as `DIREKT_NUCLEUS_OUTPUT_ROOT`; the exporter writes to `<nucleus_host><nucleus_export_root>/<job_id>/`. |

### Job source on Nucleus

`nucleus_job_root` is the single anchor — everything else is derived from it by
convention, and each derivation can be overridden individually.

| Key | Type | Notes |
|---|---|---|
| `nucleus_job_root` | string (full `omniverse://` URL) | Root folder of the job (the "Animation Export" folder). Derives all three paths below. |
| `nucleus_host` | string | Host prefix for `nucleus_export_root`. Parsed from `nucleus_job_root` if omitted. |
| `assembly_definition_url` | string | Override. Empty ⇒ discover the single `*.json` in `{nucleus_job_root}/JSON/`. |
| `animation_source_dir` | string | Override. Empty ⇒ `{nucleus_job_root}` itself (the `step_id_<M>_<N>.usd` files live in the root). |
| `model_target_dir` | string | Override. Empty ⇒ `{nucleus_job_root}/model_target/`. `prepare_job` **discovers** the `.dat`/`.xml` there — the target is no longer named in config. |

> The removed `target_id` / `target_version` / `target_file` keys are obsolete.
> `prepare_job` discovers the Vuforia model target from `model_target_dir` and
> derives `target_id` as `<dat-stem>_model_target`.

### Watching

| Key | Type | Notes |
|---|---|---|
| `watch_paths` | list of strings | **Leave empty (`[]`) to auto-derive — recommended.** See "Automatic watch-path collection" below. A non-empty list overrides the derivation entirely and is never auto-refreshed. |
| `poll_interval_sec` | int | How often `omni.client.stat()` is called **per watched path**. Default 5. With ~26 auto-derived paths a value of 1 means ~26 Nucleus stats every second — prefer 5. |
| `debounce_sec` | int | Quiet period after the *last* detected change before the pipeline fires. Default 10. Too low can fire mid-save. |

#### Automatic watch-path collection

When `watch_paths` is empty, `watcher.resolve_watch_paths()` builds the list from
the assembly definition, so it never has to be hand-maintained:

1. **`{nucleus_job_root}/JSON/*.json`** — the assembly definition itself, so
   editing it re-triggers a rebuild.
2. **`{model_target_dir}/*.dat` + `*.xml`** — so a re-trained Vuforia target
   re-triggers a rebuild.
3. **One USD per animation step** — for every step with `is_animation: true`,
   `{animation_source_dir}/step_id_<M>_<N>.usd`.

Step 3 uses `_usd_name()`, which mirrors `usd_name()` in the exporter. **These two
must stay in sync** — they are the single source of truth for the filename
convention (`step_id "1.2"` → `step_id_1_2.usd`, *no* `_Animation` suffix).

**Refresh after every rebuild.** A rebuild may consume a new
`assembly_definition.json` with steps added, removed, or re-flagged. After each
pipeline run the watcher calls `refresh_watch_paths()` to re-derive the list and
logs the delta:

```
Now watching (new): omniverse://.../step_id_24_2.usd
No longer watching: omniverse://.../step_id_9_2.usd
Watch list updated: 26 path(s) (+1, -1)
```

New paths have no `last_seen` entry, so the next poll treats them as changed and
exports them automatically. Removed paths are pruned from `.livesync_state.json`.

**Fails safe:** if the re-derive errors or returns an empty list (Nucleus hiccup,
JSON briefly unreadable), the previous list is kept — the watcher can never end
up watching nothing. Startup logs which mode is active:

```
Watching 26 path(s) on Nucleus (auto-derived; refreshed after each rebuild)
```

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

## 2. `server-kit/app/omniverse/export_glbs_from_usd.py` — runtime inputs

**No per-job editing.** The exporter is driven entirely by
`assembly_definition.json` on Nucleus plus environment variables that
`pipeline_runner` sets from `livesync.config.yaml` (section 1). Nothing about the
job or its parts is hardcoded in the script.

### Environment variables (set by `pipeline_runner`, not by hand)

| Variable | Derived from (config key) | Purpose |
|---|---|---|
| `DIREKT_JOB_ID` | `job_id` | Job id; also the Nucleus output subfolder and manifest filename stem |
| `DIREKT_NUCLEUS_OUTPUT_ROOT` | `nucleus_export_root` (+ host) | Where GLBs and `_export_report.json` are written: `<root>/<job_id>/` |
| `DIREKT_NUCLEUS_JOB_ROOT` | `nucleus_job_root` | Job folder; the assembly definition, step USDs, and model target are all derived from it |
| `DIREKT_ASSEMBLY_DEFINITION_URL` | `assembly_definition_url` (optional) | Overrides discovery of `{job_root}/JSON/*.json` |
| `DIREKT_ANIMATION_SOURCE_DIR` | `animation_source_dir` (optional) | Overrides the default step-USD location (`{job_root}`) |
| `LIVESYNC_CHANGED_URLS` | `watcher.py` | Pipe-separated changed URLs; empty = full rebuild |

### The parts come from `assembly_definition.json`

The exporter iterates `operations[].steps[]` in authoring order. For each step it
reads:

- `step_id` (e.g. `"1.2"`) → source USD `step_id_1_2.usd` (dot→underscore, **no**
  `_Animation` suffix)
- `is_animation` — `true` exports a GLB; `false` is an instruction-only step
  (recorded in the report with no GLB so the manifest still lists it)
- `instruction` — the worker-facing label (`display_name`)
- `step_type` / `component_type` — optional; used only to label the exported GLB file

There is **no `PARTS`/`PartSpec` list and no `JOB_ID`/`NUCLEUS_BASE` constant** in
the script anymore — those were removed when the exporter became
definition-driven.

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
SERVER = "omniverse://<NUCLEUS_HOST>"
REPO_ROOT = Path(__file__).resolve().parents[3]
```

- `SERVER` is the Nucleus host prefix that `nucleus_job_service` prepends
  to every path it reads from Nucleus. Must match the host portion of
  `nucleus_job_root` / `nucleus_export_root` in `livesync.config.yaml`.
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
<target>.dat`:

```
shared/samples/targets/2026-03-10.1/<target>.dat
shared/samples/targets/2026-03-10.1/<target>.xml
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
