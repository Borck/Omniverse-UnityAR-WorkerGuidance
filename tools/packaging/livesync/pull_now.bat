@echo off
REM One-click pull of already-exported GLBs from Nucleus into shared/samples.
REM No Kit, no FastAPI call -- just the download (Stage 2). Uses the project
REM venv .venv310, which has omni.client on this machine.
REM
REM Optional: pass a job id, e.g.  pull_now.bat my-other-job
REM Defaults are baked into pull_from_nucleus.py.

setlocal
set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%..\..\.."
set "PY=%REPO_ROOT%\.venv310\Scripts\python.exe"

if not exist "%PY%" (
  echo [pull_now] Python venv not found at:
  echo   %PY%
  echo Edit pull_now.bat and point PY at a Python that has omni.client.
  pause
  exit /b 1
)

if "%~1"=="" (
  "%PY%" "%SCRIPT_DIR%pull_from_nucleus.py"
) else (
  "%PY%" "%SCRIPT_DIR%pull_from_nucleus.py" --job-id %1
)

echo.
pause
endlocal
