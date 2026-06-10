@echo off
REM Run the pipeline once, right now, bypassing the watcher.
REM Useful for: (a) smoke-testing the pipeline_runner on a new host, and
REM (b) forcing a rebuild without changing the USD.
REM
REM This uses the project's regular venv Python (not Kit's), because
REM pipeline_runner.py itself is plain Python — it only shells out to Kit
REM for the GLB export step.

setlocal

set VENV_PYTHON=D:\Users\Abdul\Omniverse-UnityAR-WorkerGuidance\Omniverse-UnityAR-WorkerGuidance\.venv\Scripts\python.exe
set RUNNER=%~dp0pipeline_runner.py

if not exist "%VENV_PYTHON%" (
  echo [trigger_now] venv Python not found at:
  echo   %VENV_PYTHON%
  echo Activate or create the project venv first, or edit trigger_now.bat.
  exit /b 1
)

"%VENV_PYTHON%" "%RUNNER%"

endlocal
