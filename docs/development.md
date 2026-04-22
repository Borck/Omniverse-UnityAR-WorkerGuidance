# Development Guide

## Regenerating Protobuf Stubs

The proto contract is defined in `proto/guidance.proto`. Stubs must be regenerated
whenever the proto file changes.

### Python Stubs

```bash
pip install grpcio-tools
python -m grpc_tools.protoc \
  -I proto \
  --python_out=server-kit/app/generated \
  --grpc_python_out=server-kit/app/generated \
  proto/guidance.proto
```

### C# Stubs (Unity)

```bash
cd tools/proto-csharp
dotnet build
```

This uses `Grpc.Tools` to generate `Guidance.cs` and `GuidanceGrpc.cs` directly into
`client-unity/Assets/App/Generated/`. Commit both files after regeneration.

### Test Server Copy

The test server maintains its own copy of the proto file at `test-server/Protos/guidance.proto`.
After updating `proto/guidance.proto`, copy it:

```bash
cp proto/guidance.proto test-server/Protos/guidance.proto
```

Then rebuild the test server:

```bash
cd test-server && dotnet build
```

---

## Running Tests

### Python Server Tests

```bash
cd server-kit
pip install -r app/requirements.txt
pytest tests/ -v
```

The test suite uses the generated `guidance_pb2` stubs. Regenerate stubs before running
tests if the proto file has changed.

### Unity Tests (Editor)

1. Open the Unity project in `client-unity/`.
2. Open **Window → General → Test Runner**.
3. Select **EditMode** and click **Run All**.

Tests in `Assets/App/Tests/Editor/` run without a Vuforia or gRPC dependency — they
test state machines and utility classes only.

---

## Code Style

### Python

- Follows PEP 8 via `ruff` or `flake8`.
- Type annotations required on all public functions and class attributes.
- Docstrings in Google style.

Run:

```bash
ruff check server-kit/
```

### C# (Unity)

- Follows the existing project convention: 4-space indentation, `_camelCase` private fields,
  `PascalCase` public members.
- XML doc comments on all `public` members.
- No `using` directives that pull in assembly data (no `Resources.Load` with embedded assets).

### ASP.NET

- Follows Microsoft C# coding conventions.
- Nullable reference types enabled (`<Nullable>enable</Nullable>`).
- No logging to console in production paths — use `ILogger<T>`.

---

## Adding a New Step Field

1. Add the field to `StepActivated` in `proto/guidance.proto`.
2. Regenerate Python and C# stubs (see above).
3. Update `StepActivationDto.cs` in the Unity client if the field needs to be surfaced.
4. Update `GuidanceSessionServiceImpl.cs` in the test server to populate the field.
5. Update `grpc_session_service.py` in the Python server.
6. Update `step-definitions.yaml` schema and `StepDefinitionRepository` if the field
   comes from the step definition file.
7. Update documentation in `docs/proto-reference.md`.

---

## Environment Variable Reference

See `server-kit/app/config.py` for the complete list of environment variables and their defaults.
A template is provided in `.env.example` at the repository root.

---

## Directory Structure

```
Omniverse-UnityAR-WorkerGuidance/
├── client-unity/          Unity AR client project
│   └── Assets/App/
│       ├── Caching/       AssetCache, TargetPayloadCache
│       ├── Editor/        Build-time validation (NoEmbeddedAssemblyDataCheck)
│       ├── Generated/     Auto-generated C# protobuf stubs
│       ├── Gltf/          Model loading (IModelLoader, GltfFastModelLoader, ModelPresenter)
│       ├── Networking/    Session transport, asset streaming, DTOs
│       ├── Runtime/       AppBootstrap, AppRuntimeContext
│       ├── StateMachine/  StepCoordinator
│       ├── Telemetry/     TelemetryClient, DiagnosticsExporter
│       ├── Tests/Editor/  Editor unit tests
│       ├── UI/            HUD panels
│       └── Vuforia/       TargetManager, VuforiaTrackingBridge, VuforiaModelTargetLoader
├── docs/                  Documentation
├── proto/                 guidance.proto (canonical source)
├── server-kit/            Python gRPC + FastAPI server
│   └── app/
│       ├── generated/     Auto-generated Python protobuf stubs
│       └── ...
├── shared/samples/        Sample manifests, assets, and step definitions
├── test-server/           ASP.NET admin server
│   ├── Pages/             Razor Pages (web UI)
│   ├── Services/          gRPC service implementations
│   └── Storage/           JobStore, FileAssetStore
└── tools/
    ├── proto-csharp/      dotnet project for C# stub generation
    └── scripts/           Development helper scripts
```
