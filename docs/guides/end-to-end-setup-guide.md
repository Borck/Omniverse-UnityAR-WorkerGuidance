# End-To-End Setup Guide (M13)

Updated: 2026-03-10

## 1. Bootstrap Environment

1. Create/activate the virtual environment. Use **exactly Python 3.10** — the NVIDIA
   `omniverseclient` wheel (needed for the `/omni` Nucleus features) is only published
   for cp310; 3.11/3.12 have no matching wheel. The `py -3.10` launcher forces 3.10
   regardless of other installed Pythons. The `.venv310` name is expected by the
   LiveSync scripts (`pull_now.bat` etc.) and is git-ignored, so each machine makes
   its own — it does not come with a `git pull`.

   Windows (PowerShell), from repo root:

   - `py -3.10 -m venv .venv310`
   - `.venv310\Scripts\Activate.ps1`  (if blocked: `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`, once)
   - `python --version`  → must print `Python 3.10.x`

2. Install backend dependencies:

- `python -m pip install --upgrade pip`
- `python -m pip install -r server-kit/app/requirements.txt`
- `python -m pip install omniverseclient --extra-index-url https://pypi.nvidia.com`  (Nucleus/`/omni` features only)

3. Generate protobuf stubs:

- Python: `python -m grpc_tools.protoc -I proto --python_out=server-kit/app/generated --grpc_python_out=server-kit/app/generated proto/guidance.proto`
- C#: `dotnet build tools/proto-csharp/ProtoCSharpGen.csproj -nologo -v minimal`

4. Pull the GLB model assets (one time per machine). `shared/samples/assets/` is git-ignored, so the large GLB binaries do **not** come with `git pull` — a fresh clone has no models until they are pulled from Nucleus. Vuforia targets under `shared/samples/targets/` *are* tracked and need no pull. Requires Tier-2: `omniverseclient` in `.venv310` + Nucleus creds in `.env` (copy `.env.example`).

- Windows: run `tools\packaging\livesync\pull_now.bat`  (optionally `pull_now.bat <job-id>`; defaults are baked into `pull_from_nucleus.py`)
- Downloads the exported GLBs into `shared/samples/assets/{version}/`. Re-run only when a new asset version is exported to Nucleus.

## 2. Start Server Stack

Run both commands from the project root (`Omniverse-UnityAR-WorkerGuidance/`).

1. HTTP API (terminal 1):

- `python -m uvicorn app.server_kit_main:app --host 0.0.0.0 --port 8080 --app-dir server-kit`

2. gRPC session service (terminal 2):

- `cd server-kit && python -m app.grpc_server_main`

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
2. Complete release checklist in `docs/validation/release-checklist-and-playbooks.md`.
