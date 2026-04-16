@echo off
REM start-server.bat — Starts the Guidance Admin Test Server
REM Double-click this file to launch the server on Windows
cd /d "%~dp0"
echo Starting Guidance Admin Test Server on http://0.0.0.0:5000 ...
dotnet run --project GuidanceAdminServer.csproj
pause
