# End-To-End Setup Guide (M13)

Updated: 2026-03-10

## 1. Bootstrap Environment
1. Create/activate `.venv`.
2. Install backend dependencies:
- `python -m pip install -r server-kit/app/requirements.txt`
3. Generate protobuf stubs:
- Python: `python -m grpc_tools.protoc -I proto --python_out=server-kit/app/generated --grpc_python_out=server-kit/app/generated proto/guidance.proto`
- C#: `dotnet build tools/proto-csharp/ProtoCSharpGen.csproj -nologo -v minimal`

## 2. Start Server Stack
1. HTTP API:
- `python -m uvicorn server_kit_main:app --host 0.0.0.0 --port 8080 --app-dir server-kit/app`
2. gRPC session service:
- `python server-kit/app/grpc_server_main.py`

## 3. Build Runtime Packages
1. Optional direct package build:
- `python tools/packaging/build_runtime_packages.py --job-id job-mock-001`
2. API-triggered build:
- `POST /api/jobs/{jobId}/packages:build`

## 4. Configure Unity Client
1. Open `client-unity/` in Unity 6.
2. Add `AppBootstrap` scene object.
3. Configure transport endpoint values.
4. Ensure HUD and hint components are linked.

## 5. Execute Guidance Loop
1. Connect from app to server.
2. Receive and present active step.
3. Confirm each step progression.
4. Validate reconnect, replay, and diagnostics export.

## 6. Final Validation
1. Run backend matrix script:
- `pwsh tools/scripts/run-validation-matrix.ps1`
2. Execute pilot workflow checklist in `docs/validation/pilot-workflows-e2e.md`.
3. Complete release checklist in `docs/validation/release-checklist-and-playbooks.md`.
