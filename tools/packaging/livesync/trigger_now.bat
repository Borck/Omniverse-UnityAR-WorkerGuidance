@echo off
setlocal

set KIT_APP_DIR=D:\Omniverse\Omniverse_Apps\kit-app-template-109-0-3
set KIT_PYTHON=%KIT_APP_DIR%\_build\windows-x86_64\release\kit\python.bat
set RUNNER=%~dp0pipeline_runner.py

if not exist "%KIT_PYTHON%" (
  echo [trigger_now] Kit Python not found at "%KIT_PYTHON%"
  echo Edit trigger_now.bat to set KIT_PYTHON to the correct path.
  exit /b 1
)

"%KIT_PYTHON%" "%RUNNER%"
endlocal
