@echo off
REM Run the pipeline once, right now, bypassing the watcher.
REM Useful for: (a) smoke-testing the pipeline_runner on a new host, and
REM (b) forcing a rebuild without changing the USD.
REM
REM Uses Kit's bundled Python because pipeline_runner now imports
REM nucleus_job_service, which depends on omni.client.

setlocal

set KIT_APP_DIR=D:\Omniverse\Omniverse_Apps\kit-app-template-109-0-3
set KIT_PYTHON=%KIT_APP_DIR%\_build\windows-x86_64\release\kit\python.bat
set RUNNER=%~dp0pipeline_runner.py

if not exist "%KIT_PYTHON%" (
  echo [trigger_now] Kit Python not found at:
  echo   %KIT_PYTHON%
  echo Edit trigger_now.bat and set KIT_PYTHON to the correct path.
  exit /b 1
)

"%KIT_PYTHON%" "%RUNNER%"

endlocal
