# Minimal Headless GLB-Export Kit App (`direkt_export.kit`)

How to build, register, and deploy the lightweight Omniverse Kit app that runs
the USD→GLB export headlessly — and how to stand it up on a **new machine with a
different Kit install and a different Nucleus server**.

---

## 1. Why this exists

The live-sync pipeline exports per-part GLBs by running
`server-kit/app/omniverse/export_glbs_from_usd.py` inside Omniverse Kit.
Launching the **full USD Composer** app for this loads hundreds of extensions
(RTX renderer, Hydra, viewport, UI, physics) and compiles shaders — ~40 s cold /
~20–25 s warm — none of which a data-only export uses.

`direkt_export.kit` is a **minimal headless app** that loads only the four
extensions the export actually needs. Result: a full 5-part rebuild runs in
**~5–6 s** with no RTX shader compilation; an incremental live-sync run (one
changed part) is ~1–2 s.

---

## 2. The four things that must ALL be in place

Miss any one and the export silently runs the full app, fails to launch, or
produces no GLBs:

1. The **`.kit` file** in `source/apps/` — the app definition (§3).
2. **Registration** in `premake5.lua` — so the build produces an entrypoint (§4).
3. A **`repo.bat build`** — creates `_build/.../direkt_export.kit.bat` (§5).
4. The **pump-loop tail** in `export_glbs_from_usd.py` — so the minimal app does
   not quit before the export runs (§6). **This one is easy to miss and the
   export appears to "work" without it while doing nothing.**

---

## 3. The `.kit` file

Path: `<kit-app-template>/source/apps/direkt_export.kit`

```toml
[package]
title = "DIREKT GLB Exporter"
description = "Headless minimal Kit app for USD->GLB export (no renderer/UI)."
version = "1.0.0"
keywords = ["app"]

[dependencies]
"omni.usd" = {}                 # open + flatten the stage
"omni.kit.asset_converter" = {} # USD -> GLB conversion
"omni.client" = {}              # Nucleus I/O (usually pulled by omni.usd; explicit is safe)
"omni.kit.async_engine" = {}    # pumps the exec script's asyncio task

[settings]
app.window.hideUi = true
app.runLoops.main.rateLimitEnabled = true
app.runLoops.main.rateLimitFrequency = 60
app.runLoops.main.rateLimitUseBusyLoop = false

[[test]]
args = ["--no-window", "--no-assert-dialog"]
```

No RTX/Hydra/viewport/UI/physics are listed, so none are loaded. If the
converter ever reports a missing extension, add **only that one name** to
`[dependencies]` — start minimal, add exactly what Kit asks for.

---

## 4. Register the app in the build

Kit only builds apps that are **declared**. Two edits:

**Required — `<kit-app-template>/premake5.lua`.** Add one line mirroring the
existing app:

```lua
-- existing app in the template:
define_app("direkt.my_usd_composer.kit")
-- add ours (note: the ".kit" suffix is part of the name here):
define_app("direkt_export.kit")
```

**Recommended — `<kit-app-template>/repo.toml`.** Add the app to the extension
precache list so a fresh machine does not pull extensions from the online
registry on first launch. Under `[repo_precache_exts]`:

```toml
apps = [
    "${root}/source/apps/direkt.my_usd_composer.kit",
    "${root}/source/apps/direkt_export.kit",
]
```

### Commands to register + build (copy-paste)

Set the two paths for the machine, then run in order. These perform steps 3–5
(place the `.kit`, register it in `premake5.lua`, add it to the precache list)
and build:

```powershell
# --- adjust these two for the machine ---
$KIT  = "D:\Omniverse\Omniverse_Apps\kit-app-template-109-0-3"                                   # kit-app-template root
$REPO = "D:\Users\Abdul\Omniverse-UnityAR-WorkerGuidance\Omniverse-UnityAR-WorkerGuidance"        # this repo checkout

# 1. Place the app definition into the template's source/apps.
#    (Keep the git-tracked master in the repo; here we copy it in.
#     If you have no master yet, create the file with the §3 contents.)
Copy-Item "$REPO\server-kit\app\omniverse\direkt_export.kit" "$KIT\source\apps\direkt_export.kit" -Force

# 2. Register it in the build -- THIS is what creates the launchable entrypoint.
Add-Content -Path "$KIT\premake5.lua" -Value 'define_app("direkt_export.kit")'

# 3. (Recommended) add it to the extension precache list in repo.toml.
#    Literal string replace -- adjust the existing app name if your template's
#    default app is not "direkt.my_usd_composer.kit".
$toml = "$KIT\repo.toml"
$old  = 'apps = ["${root}/source/apps/direkt.my_usd_composer.kit"]'
$new  = 'apps = ["${root}/source/apps/direkt.my_usd_composer.kit", "${root}/source/apps/direkt_export.kit"]'
$text = (Get-Content $toml -Raw).Replace($old, $new)
[System.IO.File]::WriteAllText($toml, $text, (New-Object System.Text.UTF8Encoding($false)))

# 4. Build -- produces _build\windows-x86_64\release\direkt_export.kit.bat and
#    precaches the app's extensions into the local registry cache.
Push-Location $KIT; .\repo.bat build; Pop-Location

# 5. Verify the entrypoint was produced (must print True).
Test-Path "$KIT\_build\windows-x86_64\release\direkt_export.kit.bat"

# 6. (Optional) test-launch the minimal app directly.
Push-Location $KIT
.\repo.bat launch --name direkt_export.kit -- --no-window --exec "$REPO\server-kit\app\omniverse\export_glbs_from_usd.py"
Pop-Location
```

Notes:
- `Add-Content` (step 2) is idempotent-unsafe — running it twice adds the line
  twice. If you re-run, first check `premake5.lua` doesn't already contain
  `define_app("direkt_export.kit")`.
- Step 3 is optional but skips a one-time ~10 s "Pulling extension from the
  registry" on the very first launch. If you skip it, the first run just pulls
  the extensions online and caches them.
- `repo.bat build` pulls any missing extensions from the **Omniverse extension
  registries** listed in `repo.toml` (`[repo_precache_exts].registries`) — so
  the build machine needs internet access the first time (or a mirrored/local
  registry).

---

## 5. Build

```powershell
cd <kit-app-template>
.\repo.bat build
```

This produces the launch entrypoint
`_build\windows-x86_64\release\direkt_export.kit.bat` and (if added to the
precache list) downloads and version-locks the app's extensions. First build
takes a few minutes; later builds are seconds.

Verify the entrypoint exists:

```powershell
Test-Path "<kit-app-template>\_build\windows-x86_64\release\direkt_export.kit.bat"
```

Must return `True`. If it is missing, the `premake5.lua` line did not take —
re-check §4 and rebuild.

---

## 6. The pump-loop tail (CRITICAL — do not skip)

The minimal app **quits the instant the `--exec` script returns**, before the
scheduled async export coroutine ever runs. Symptom: the Kit step exits in ~3 s
with **no `[step-…]` lines**, and the pipeline silently reuses stale GLBs.

The export script must keep the app alive by pumping its update loop until the
export finishes. The tail of `export_glbs_from_usd.py` MUST be:

```python
async def _run_and_signal(result: dict) -> None:
    code = 0
    try:
        await run()
    except Exception as exc:
        print(f"[export] FATAL: {exc}")
        code = 1
    finally:
        result["code"] = code


import omni.kit.app
import time as _time

_result: dict = {"code": None}
asyncio.ensure_future(_run_and_signal(_result))

_kit_app = omni.kit.app.get_app()
_deadline = _time.time() + 80.0          # wall-clock backstop
while _result["code"] is None and _time.time() < _deadline:
    _kit_app.update()

import sys
sys.stdout.flush()
sys.stderr.flush()
os._exit(_result["code"] if _result["code"] is not None else 1)
```

**Do NOT gate the loop on `app.is_running()`** — it is `False` during `--exec`,
so the loop exits immediately, the script returns, and the app shuts down
mid-export. Gate on the task's own completion flag (`_result["code"]`).

---

## 7. Launch command

Standalone:

```
repo.bat launch --name direkt_export.kit -- --no-window --exec <...>\export_glbs_from_usd.py
```

In the live-sync pipeline this lives in `livesync.config.yaml` as
`kit_export_command`. **Both lines of the folded (`>`) block must have identical
indentation** — a mismatch is a `yaml.parser.ParserError`:

```yaml
kit_export_command: >
  cmd /c "cd /d {kit_app_dir} && repo.bat launch --name direkt_export.kit -- --no-window
  --exec {repo_root}/server-kit/app/omniverse/export_glbs_from_usd.py"
```

---

## 8. Deploying to a NEW device (different Kit + Nucleus)

The `.kit` app itself is portable and git-tracked. What changes **per machine**:

| What | Where | Set to |
|---|---|---|
| Kit template location | `livesync.config.yaml` → `kit_app_dir` | the new machine's kit-app-template path |
| Repo location | `livesync.config.yaml` → `repo_root` | the new repo checkout path |
| Nucleus source + output (Kit side) | `export_glbs_from_usd.py` → `NUCLEUS_BASE`, `NUCLEUS_OUTPUT_ROOT` | the new Nucleus host + folders |
| Nucleus host (server side) | `server-kit/app/core/config.py` → `SERVER` | the new Nucleus host (`omniverse://<ip>`) |
| Watched USDs + export root | `livesync.config.yaml` → `watch_paths`, `nucleus_export_root` | the new Nucleus paths |
| Kit's bundled Python | `trigger_now.bat`, `run_watcher.bat` → `KIT_PYTHON` | `_build\...\kit\python.bat` under the new Kit |

Steps on the new machine:

1. Install / locate the **kit-app-template** (same major version — e.g. 109.x).
2. Copy `direkt_export.kit` into its `source/apps/`.
3. Add `define_app("direkt_export.kit")` to its `premake5.lua`, and the precache
   entry to `repo.toml` (§4).
4. `repo.bat build` (§5) — verify the `.kit.bat` entrypoint appears.
5. Ensure `export_glbs_from_usd.py` has the **pump-loop tail** (§6) and the new
   `NUCLEUS_BASE` / `NUCLEUS_OUTPUT_ROOT` / `JOB_ID` / `PARTS`.
6. Update `livesync.config.yaml` paths and IPs (table above).
7. **Authenticate `omni.client` to the new Nucleus** — it reads cached Omniverse
   credentials from the host profile (`~/.nvidia-omniverse/` /
   `%LOCALAPPDATA%\ov`). Log in once via the Omniverse Launcher/Nucleus on that
   host, or provide credentials programmatically.
8. Run `trigger_now.bat` and verify (§9).

---

## 9. Verify a good run

```
=== Kit GLB export ===
[step-001] Opening omniverse://<host>/.../Plate_Bottom.usd
[step-001] Wrote  omniverse://<host>/.../<job>/Plate_Bottom.glb
...
=== Summary ===
exported=5  skipped=0  failed=0
```

Checklist:
- The Kit-export step takes **seconds with `[step-…]` lines** — an exit in ~3 s
  with **no** `[step-…]` lines means the export did not run (see §6).
- GLBs + `_export_report.json` appear on Nucleus under
  `<NUCLEUS_OUTPUT_ROOT>/<JOB_ID>/`, freshly written.
- `prepare_job` then downloads them into `shared/samples/assets/` (what FastAPI
  serves) and writes the manifest + `step-definitions.yaml`.

---

## 10. Troubleshooting (issues seen during bring-up)

| Symptom | Cause | Fix |
|---|---|---|
| `direkt_export.kit is missing the built entrypoint script … Have you built your app?` | App not registered in `premake5.lua` | Add `define_app("direkt_export.kit")`, then `repo.bat build` (§4–5) |
| Kit export exits ~3 s, **no** `[step-…]` lines; pipeline reuses stale GLBs | Minimal app quits before the async export runs | Add the pump-loop tail (§6) |
| `[exit 0]` right after `[step-001] Opening…` | Loop gated on `is_running()` (False during `--exec`) | Gate on the completion flag, not `is_running()` (§6) |
| `yaml.parser.ParserError … expected <block end>` | Inconsistent indentation in `kit_export_command` | Make both folded-block lines the same indent (§7) |
| First launch spends ~10 s "Pulling extension … from the registry" | App not in the precache list | Add to `[repo_precache_exts].apps` in `repo.toml`, rebuild (one-time) (§4) |
| Full USD Composer loads (40 s, RTX/shader compile) instead of the minimal app | `--name direkt_export.kit` not resolving | App not built/registered — §4–5 |
| `trigger_now.bat` "pops up and closes instantly" | An error before the export (bad `KIT_PYTHON`, YAML, missing PyYAML) | Run it from an open terminal to read the error; check the pipeline log |

---

## 11. Files involved

- `<kit-app-template>/source/apps/direkt_export.kit` — the app definition (§3)
- `<kit-app-template>/premake5.lua` — app registration (§4)
- `<kit-app-template>/repo.toml` — extension precache list (§4)
- `server-kit/app/omniverse/export_glbs_from_usd.py` — export script + pump-loop tail (§6); also `NUCLEUS_BASE`, `NUCLEUS_OUTPUT_ROOT`, `JOB_ID`, `PARTS`
- `server-kit/app/omniverse/nucleus_job_service.py` — `prepare_job` (downloads GLBs from Nucleus → FastAPI asset store)
- `tools/packaging/livesync/livesync.config.yaml` — `kit_export_command` + all per-machine paths (§7–8)
- `tools/packaging/livesync/pipeline_runner.py` — orchestrates the Kit export then `prepare_job`
- `tools/packaging/livesync/trigger_now.bat` / `run_watcher.bat` — entry points; set `KIT_PYTHON`

---

## 12. Data flow (unchanged by the minimal app)

The minimal app changes only *how fast* Kit boots, not the data path:

```
USD (Nucleus)
  → open + flatten to a local temp flat.usd        (scratch only)
  → convert → GLB written to Nucleus               (<NUCLEUS_OUTPUT_ROOT>/<JOB_ID>/*.glb)   [Kit export]
  → prepare_job downloads GLB from Nucleus
  → shared/samples/assets/<version>/               (served by FastAPI)                      [prepare_job]
```

GLBs always go **USD → Nucleus → FastAPI**; there is no direct USD → FastAPI
path. (`flat.usd` is a throwaway local temp used only to speed up flattening.)
