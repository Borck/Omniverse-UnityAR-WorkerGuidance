# Live-sync watcher (Omniverse side)

Polls Nucleus for changes to the master USD (and its part sublayers), debounces
saves made in rapid succession, and triggers the existing GLB-and-manifest
pipeline. The FastAPI server then sees a new manifest and propagates the new
`assetVersion` to Unity (Vuzix) over the existing gRPC stream.

```
USD save on Nucleus
   |
   v
watcher.py        (polls every 5s, debounces 10s)
   |
   v
pipeline_runner.py
   ├── Kit headless: export_glbs_from_usd.py            -> GLBs on Nucleus
   └── nucleus_job_service.prepare_job() (in-process)   -> download + manifest
```

## Layout

```
livesync/
├── README.md                      (this file)
├── livesync.config.example.yaml   (template — copy to livesync.config.yaml)
├── watcher.py                     (long-running polling loop)
├── pipeline_runner.py             (one-shot orchestrator)
├── run_watcher.bat                (entry point — uses Kit's Python)
└── trigger_now.bat                (manual rebuild — also uses Kit's Python)
```

Both the watcher and pipeline_runner run in Kit's bundled Python because they
import `omni.client`: the watcher to stat Nucleus, the pipeline_runner to
download GLBs and call `nucleus_job_service.prepare_job()` directly.

## First-time setup on a new host

1. Copy the config template and edit it:

   ```powershell
   copy livesync.config.example.yaml livesync.config.yaml
   notepad livesync.config.yaml
   ```

   The fields most likely to need changing per host:
   - `repo_root` — where this repo is checked out
   - `kit_app_dir` — where `kit-app-template-109-0-3` lives
   - `kit_export_command` — verify the flag syntax matches your template
   - `nucleus_export_root` — Nucleus path where the Kit exporter drops GLBs
     (matches `NUCLEUS_OUTPUT_ROOT` in `export_glbs_from_usd.py`, minus the
     `omniverse://<host>` prefix)
   - `target_version` / `target_file` — Vuforia Model Target identifiers
   - `watch_paths` — the master USD plus every part USD and `-Position.usd`

2. Confirm the Kit headless export works by itself, by hand:

   ```powershell
   cd D:\Omniverse\Omniverse_Apps\kit-app-template-109-0-3
   .\repo.bat launch -- --no-window --exec D:\DIREKT\Worker guidance\Omniverse-UnityAR-WorkerGuidance\server-kit\app\omniverse\export_glbs_from_usd.py
   ```

   If this exits cleanly and produces GLBs + `_export_report.json` on
   Nucleus (at `omniverse://<host>/Users/shahan/<job_id>/`), paste the
   working command into `kit_export_command` in the config.

   If the flag syntax differs in your template, this is the ONE line to adjust.

3. Smoke-test the full pipeline once, without the watcher:

   ```powershell
   .\trigger_now.bat
   ```

   This runs `pipeline_runner.py` directly: Kit export → in-process call to
   `nucleus_job_service.prepare_job()` (which downloads the GLBs, hashes
   them, writes the versioned manifest, and updates step-definitions.yaml).
   Check `logs/runs/pipeline-<timestamp>.log` for the output. If both
   stages succeed, the pipeline is wired up correctly.

4. Edit `run_watcher.bat` and confirm `KIT_PYTHON` points to a real file.
   Standard location in kit-app-template-109-0-3:

   ```
   _build\windows-x86_64\release\kit\python.bat
   ```

5. Start the watcher:

   ```powershell
   .\run_watcher.bat
   ```

   You should see `Live-sync watcher starting` and `Watching N path(s)`.

6. To run the watcher on boot, add it as a Windows Task Scheduler entry:
   - Trigger: At startup
   - Action: Start a program → `run_watcher.bat`
   - Settings: Restart on failure, run whether user logged in or not

## Testing the live loop

1. Start the watcher (`.\run_watcher.bat`).
2. Open the master USD in USD Composer or your editor of choice.
3. Make a small change and save.
4. Watch `logs/watcher.log` — within ~5 seconds you should see:
   ```
   Change detected: omniverse://...
   ```
5. After 10 seconds of quiet (the debounce window), the pipeline runs and the
   log shows the Kit export starting. Total time from save to new manifest:
   typically 60–90 seconds.

## Moving to a different machine

Edit `livesync.config.yaml` — every path and every command is in there. No
code changes should be needed. If they are, that's a bug; please open an issue.
