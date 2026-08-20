# Live-Sync Operations Guide

Step-by-step runbook for bringing the live-sync subsystem up on a host,
verifying it end-to-end, and diagnosing common failures.

Companion to [architecture.md](architecture.md), [components.md](components.md),
and [configuration.md](configuration.md).

## 0. Prerequisites

A host running the live-sync subsystem needs:

- **Windows 10/11 64-bit**. The pipeline is Windows-only today because
  Kit App Template 109 ships only for Windows + Linux, and the watcher
  uses `.bat` entry points.
- **Omniverse Kit App Template 109.0.3** built once via `repo.bat build`
  so `_build/windows-x86_64/release/kit/python.bat` exists.
- **Python 3.10+** project venv with `pip install -r requirements.txt`
  (FastAPI, gRPC, watchdog, pydantic, etc.).
- **Network reachability and cached credentials** for the Nucleus host
  the artist saves to. Log in once via Omniverse Launcher or Kit's
  Connect window from the Windows account that will run the watcher.
- **Vuforia Engine 11.4.4 .tgz** placed at
  `client-unity/Packages/com.ptc.vuforia.engine-11.4.4.tgz` (Unity
  developers only; not needed on the server host).

## 1. First-time setup on a new host

### 1.1 Clone the repo

```powershell
git clone https://github.com/Borck/Omniverse-UnityAR-WorkerGuidance.git
cd Omniverse-UnityAR-WorkerGuidance
git checkout abdul-automatic-omniverse-change-detection
```

### 1.2 Create the project venv

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
pip install grpcio-tools     # needed only for regenerating proto stubs
```

### 1.3 Install PyYAML inside Kit's bundled Python

The watcher reads YAML config. Kit's Python doesn't ship with PyYAML:

```powershell
& "<KIT_APP_DIR>\_build\windows-x86_64\release\kit\python.bat" -m pip install pyyaml
```

Replace `<KIT_APP_DIR>` with the actual path, e.g.
`D:\Omniverse\Omniverse_Apps\kit-app-template-109-0-3`.

### 1.4 Configure the watcher

```powershell
cd tools\packaging\livesync
copy livesync.config.example.yaml livesync.config.yaml
notepad livesync.config.yaml
```

Edit at minimum: `repo_root`, `kit_app_dir`, `nucleus_job_root`,
`nucleus_export_root`. Leave **`watch_paths: []`** so the watch list is derived
from `assembly_definition.json` and refreshed after every rebuild.

The old `target_version` / `target_file` keys are obsolete — the Vuforia model
target is discovered from `{nucleus_job_root}/model_target/`. See
[configuration.md](configuration.md) section 1 for every field.

### 1.5 Configure the job (no script editing)

The exporter is **definition-driven** — you do not edit
`export_glbs_from_usd.py` to add or change a job. All per-job values come from
`livesync.config.yaml` and the job's `assembly_definition.json` on Nucleus.

In `livesync.config.yaml` verify:

- `job_id` — the job identifier (manifest stem, Nucleus output subfolder, gRPC key)
- `nucleus_job_root` — the job folder on Nucleus; the assembly definition
  (`{job_root}/JSON/*.json`), the step USDs (`step_id_<M>_<N>.usd`), and the model
  target (`{job_root}/model_target/`) are all derived from it
- `nucleus_export_root` — a writable Nucleus folder for the exported GLBs

The step list, order, and which steps are animated (`is_animation`) live in
`assembly_definition.json`, not in any Python file. See
[configuration.md](configuration.md) section 2.

### 1.6 Place Vuforia target files

```
shared/samples/targets/<target_version>/<target_file>      (e.g. .dat)
shared/samples/targets/<target_version>/<target_xml>       (e.g. .xml)
```

Generated offline in Vuforia Target Manager.

## 2. Smoke-testing each layer

Bring the system up in stages so a failure at one level doesn't get
confused with a failure at another. Stop after any step that fails.

### 2.1 Verify Kit's Python is reachable

```powershell
& "<KIT_APP_DIR>\_build\windows-x86_64\release\kit\python.bat" -c "import omni.client; print(omni.client)"
```

Expected: a non-empty module path. Failure: build the Kit App Template
first (`.\repo.bat build` inside the Kit App Template directory).

### 2.2 Verify Nucleus auth

```powershell
& "<KIT_APP_DIR>\_build\windows-x86_64\release\kit\python.bat"
```

Then at the prompt:

```python
import omni.client
result, entry = omni.client.stat("omniverse://<your-host>/Users/<you>")
print(result, entry)
```

Expected: `omni.client.Result.OK`. Failure (`ERROR_ACCESS_DENIED`): log
in via Omniverse Launcher under the same Windows user that will run the
watcher.

### 2.3 Verify the Kit export works in isolation

```powershell
cd <KIT_APP_DIR>
.\repo.bat launch -- --no-window --exec "<REPO_ROOT>\server-kit\app\omniverse\export_glbs_from_usd.py"
```

Expected output ends with:

```
=== Summary ===
exported=N  skipped=0  failed=0
OK   step-001  <part>
...
```

…and a Nucleus listing of `<nucleus_export_root>/<job_id>/` should show
one `.glb` per part plus `_export_report.json`.

Failure: see section 4 for common Kit / Nucleus errors.

### 2.4 Verify the full pipeline manually

```powershell
cd <REPO_ROOT>\tools\packaging\livesync
.\trigger_now.bat
```

Expected output:

```
[pipeline_runner] Log: ...\pipeline-<timestamp>.log
=== Kit GLB export === ...
[pipeline_runner] Kit GLB export -> exit 0 in ~30s
=== nucleus_job_service.prepare_job === ...
[NucleusJobService] Wrote manifest: ...\<job_id>.manifest.json
[pipeline_runner] nucleus_job_service.prepare_job -> ok in Ns (steps_synced=N)
```

Verify on disk:

```powershell
dir ..\..\..\shared\samples\manifests\<JOB_ID>.manifest.json
dir ..\..\..\shared\samples\assets\sha256_*
```

### 2.5 Verify the watcher detects changes

```powershell
cd <REPO_ROOT>\tools\packaging\livesync
.\run_watcher.bat
```

Expected first lines:

```
[run_watcher] Using Python: ...\python.bat
[run_watcher] Starting watcher: ...\watcher.py
YYYY-MM-DD HH:MM:SS INFO Live-sync watcher starting
YYYY-MM-DD HH:MM:SS INFO Watching N path(s) on Nucleus
```

Then in another window, edit one of the watched USDs in USD Composer
(or any editor that saves to Nucleus). The watcher should log:

```
INFO Change detected: omniverse://...
INFO Debounce window elapsed (10s quiet); running pipeline for 1 changed path(s)
INFO Triggering pipeline (incremental: 1 changed path(s)): ...
... (pipeline output) ...
INFO Pipeline finished with exit code 0
```

### 2.6 Verify the FastAPI and gRPC servers start cleanly

```powershell
# Terminal A — FastAPI
cd <REPO_ROOT>
python -m uvicorn app.server_kit_main:app --host 0.0.0.0 --port 8080 --app-dir server-kit

# Terminal B — gRPC server
cd <REPO_ROOT>\server-kit
python -m app.grpc_server_main
```

Expected from FastAPI:

```
omniverse router not loaded (...) /omni endpoints disabled.   ← only on hosts without omni.client
http service started
INFO:     Application startup complete.
```

Expected from gRPC:

```
manifest_watcher seeded job_count=1 poll_interval_sec=2.0
grpc service started
```

### 2.7 Verify ManifestUpdated push end-to-end

With the watcher, FastAPI, gRPC server, and a gRPC client (Unity or
`grpcurl`) all running, save one of the watched USDs. After the pipeline
completes (~30s for incremental, ~45s for full rebuild), the gRPC server
log should print:

```
manifest_updated job=<JOB_ID> changed_steps=N sessions_notified=M
```

`sessions_notified` is the number of clients currently subscribed to that
job. If it's 0, no client is connected — start a client first.

## 3. Regenerating proto stubs

Required whenever `proto/guidance.proto` changes.

### Python (FastAPI/gRPC side)

```powershell
cd <REPO_ROOT>
& "<REPO_ROOT>\.venv\Scripts\python.exe" -m grpc_tools.protoc `
    -I proto `
    --python_out=server-kit/app/generated `
    --grpc_python_out=server-kit/app/generated `
    proto/guidance.proto
```

Successful output is silent. Verify:

```powershell
Select-String -Path "<REPO_ROOT>\server-kit\app\generated\guidance_pb2.py" `
    -Pattern "ManifestUpdated" | Select-Object -First 3
```

### C# (Unity client side)

```powershell
cd <REPO_ROOT>\tools\proto-csharp
dotnet build
```

Outputs to `client-unity/Assets/App/Generated/`. Verify:

```powershell
Select-String -Path "<REPO_ROOT>\client-unity\Assets\App\Generated\Guidance.cs" `
    -Pattern "ManifestUpdated" | Select-Object -First 3
```

### Mirror to the test server

`test-server/Protos/guidance.proto` must match `proto/guidance.proto`
exactly. Any change to one needs the same change in the other; otherwise
the test server speaks a slightly different dialect than production.

## 4. Common failures

### `<< was unexpected at this time.`

A `.bat` file contains literal git merge conflict markers
(`<<<<<<<`, `=======`, `>>>>>>>`). cmd.exe sees `<<` and dies before
running anything. Search the file for the markers and resolve them.

### `[pipeline_runner] Kit GLB export timed out after 120s`

Two possible causes:

1. **Kit didn't exit on its own.** The export script's `_run_and_quit`
   uses `os._exit(0)` to force termination. If a previous edit removed
   that, Kit will idle forever after the export finishes. Restore
   `os._exit` and rerun.
2. **Kit is genuinely slow on first boot.** Kit's first-time extension
   compilation can take 1–3 minutes. Bump `pipeline_timeout_sec` to
   600 or 1800 in `livesync.config.yaml`.

### `Error while polling Nucleus: 'ListEntry' object has no attribute 'modified_time_ns'`

The Kit Python in use exposes `modified_time` (datetime) instead of
`modified_time_ns` (int). `watcher.py` handles both — make sure you've
pulled the latest. If you still see it, check that `stat_nucleus_mtime`
in `watcher.py` has the `getattr(entry, "modified_time", None)` fallback.

### `ModuleNotFoundError: No module named 'omni.client'`

The script invoking it is running in the project venv, not Kit's Python.
For `watcher.py` and `pipeline_runner.py` this is a bug — they must
launch from `run_watcher.bat` / `trigger_now.bat` which point at Kit's
Python. For FastAPI (`server_kit_main.py`) this is benign — the import
is now guarded with try/except and the server boots without the `/omni`
endpoints.

### `ModuleNotFoundError: No module named 'yaml'`

PyYAML isn't installed in Kit's bundled Python. Install it once:

```powershell
& "<KIT_APP_DIR>\_build\windows-x86_64\release\kit\python.bat" -m pip install pyyaml
```

### `_omni_copy_to_local failed` / `Cannot read /Users/...`

Path mismatch between `nucleus_export_root` in `livesync.config.yaml`
and `NUCLEUS_OUTPUT_ROOT` in `export_glbs_from_usd.py`. They must point
at the same Nucleus folder (one as path-with-host-stripped, the other as
full URL).

### `Could not download local file 'omniverse://.../<file>.usd/<other>.usd'`

The resolved animation-source directory (`nucleus_job_root` /
`animation_source_dir`) is being treated as a USD file (with `.usd`
extension) and the step URL is built underneath it as if it were a
folder. It must be the *folder* containing the `step_id_<M>_<N>.usd`
files, never a USD file URL.

### Manifest changes but `manifest_updated` is not logged

Open `shared/samples/manifests/<JOB_ID>.manifest.json` and confirm at
least one step's `assetVersion` actually changed. If all hashes match
the previous values, `manifest_watcher`'s diff finds no change and
*correctly* sends no message. This is the expected behavior for no-op
saves.

### `sessions_notified=0` despite a connected client

The client connected but didn't send `hello` with a `capabilities` string
containing `job=<JOB_ID>`. Without that, the session is routed to the
default mock job, not the one the watcher is updating. Check the Unity
side's `HelloRequest` construction.

### Unity Vuforia errors (`The type 'Vuforia' could not be found`)

`com.ptc.vuforia.engine-11.4.4.tgz` is missing from
`client-unity/Packages/`. Download from <https://developer.vuforia.com/downloads/>
and place it at that exact path. The version must match the pin in
`client-unity/Packages/manifest.json`.

## 5. Operational notes

### Running on boot

Put `run_watcher.bat`, the FastAPI uvicorn invocation, and
`grpc_server_main.py` behind Windows Task Scheduler entries:

- **Trigger:** *At system startup*
- **Action:** *Start a program* → the `.bat` or `python -m ...` command
- **Settings:** *Run whether user is logged on or not*, *Restart on
  failure every 1 minute*

For FastAPI and gRPC specifically, NSSM (the Non-Sucking Service Manager)
is a cleaner option — gives proper Windows Service semantics with
auto-restart on crash.

### Disk hygiene

`nucleus_job_service.prepare_job` automatically deletes any
`sha256_<hash>/` folder that isn't referenced by the current manifest
on each rebuild. No manual cleanup needed. The orphan removal log line is:

```
[NucleusJobService] Cleaned up N orphaned version(s)
```

### Log files to watch

| File | Contains |
|---|---|
| `tools/packaging/livesync/logs/watcher.log` | Watcher detections, pipeline triggers, summary results. Rotates at 2 MB. |
| `tools/packaging/livesync/logs/runs/pipeline-<ts>.log` | Full Kit + prepare_job output per rebuild. One file per trigger. |
| Kit's own log | `~/AppData/Local/ov/data/Kit/...` (per Kit App Template config). Useful when Kit itself fails to boot. |
| gRPC server stdout | `manifest_updated` and `sessions_notified` lines. |
| FastAPI stdout | HTTP request logs. |

### Stopping the watcher cleanly

`Ctrl+C` in the `run_watcher.bat` window. The watcher catches
`KeyboardInterrupt` and exits with code 0. Any in-flight pipeline
subprocess continues to completion — it isn't killed.

### Stopping a stuck Kit

If a pipeline run hangs (e.g., before the `os._exit` patch was pulled):

```powershell
Get-Process kit -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process | Where-Object { $_.ProcessName -match "kit|repo" } | Stop-Process -Force
```

The orphan Kit can also keep the Nucleus KVDB locked, blocking the next
run with a warning like:

```
[omni.kvdb.plugin] Disabling key-value database because another kit process is locking it
```

Killing the orphan resolves it.
