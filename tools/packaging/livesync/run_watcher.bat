@echo off
REM Launches the live-sync watcher using Kit's bundled Python so that
REM `import omni.client` works. Run this once at boot (Windows Task Scheduler
REM is the easy option) and leave it alone.
REM
REM The exact path to Kit's Python depends on your kit-app-template version.
REM Adjust KIT_PYTHON below if needed. On AT21 with kit-app-template-109-0-3,
REM the standard location is `_build\...\kit\python.bat`; verify and update.

setlocal

set KIT_APP_DIR=D:\Omniverse\Omniverse_Apps\kit-app-template-109-0-3
set KIT_PYTHON=%KIT_APP_DIR%\_build\windows-x86_64\release\kit\python.bat
set WATCHER=%~dp0watcher.py

if not exist "%KIT_PYTHON%" (
  echo [run_watcher] Kit Python not found at:
  echo   %KIT_PYTHON%
  echo Edit run_watcher.bat and set KIT_PYTHON to the correct path.
  exit /b 1
)

echo [run_watcher] Using Python: %KIT_PYTHON%
echo [run_watcher] Starting watcher: %WATCHER%
"%KIT_PYTHON%" "%WATCHER%"

endlocal
