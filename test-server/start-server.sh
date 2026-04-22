#!/usr/bin/env bash
# start-server.sh — Starts the Guidance Admin Test Server
# Usage: bash start-server.sh
set -e
cd "$(dirname "$0")"
echo "Starting Guidance Admin Test Server on http://0.0.0.0:5000 ..."
dotnet run --project GuidanceAdminServer.csproj
