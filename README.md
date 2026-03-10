# Omniverse Unity AR Worker Guidance

This repository contains the first implementation slice for an AR worker guidance system.

## Components
- `server-kit/`: Omniverse Kit server for step resolution, export, gRPC, and HTTP services.
- `client-unity/`: Unity 6 client runtime for session orchestration, model loading, and tracking.
- `proto/`: Shared protobuf contracts used by server and client.
- `shared/`: Shared schemas, sample payloads, and fixtures.

## Current Status
- Repository skeleton created.
- Initial protobuf contract drafted.
- Developer workspace/tasks scaffolded.
- Minimal health endpoint server skeleton added.
- App factory + environment config + structured server logging added.
- Mock gRPC session stub added (hello, heartbeat/ping, mock step activation).
- Manifest and asset HTTP endpoints added with cache headers.
- External YAML step-definition source added.
- Asset transfer gRPC contract added for GLB chunk streaming.
- Unity HTTP bridge transport now supports connect/heartbeat with periodic heartbeat and reconnect loop from `AppBootstrap`.

## Quick Start
1. Open `Omniverse-UnityAR-WorkerGuidance.code-workspace` in VS Code.
2. Review and install recommended extensions.
3. Create/activate a Python environment and install dependencies:
	- `python -m pip install -r server-kit/app/requirements.txt`
4. Generate protobuf outputs:
	- `python -m grpc_tools.protoc -I proto --python_out=server-kit/app/generated --grpc_python_out=server-kit/app/generated proto/guidance.proto`
	- `dotnet build tools/proto-csharp/ProtoCSharpGen.csproj -nologo -v minimal`
5. Run the HTTP server:
	- `python -m uvicorn server_kit_main:app --host 0.0.0.0 --port 8080 --app-dir server-kit/app`
6. Run the mock gRPC session service:
	- `python server-kit/app/grpc_server_main.py`
7. Connect Unity transport natively to gRPC server (no proxy):
	- Unity gRPC target should be `<host-or-lan-ip>:50051`
	- In `AppBootstrap`, keep `useNativeGrpcTransport=true`
8. Optional HTTP bridge fallback (if native gRPC is not available in your runtime profile):
	- Disable `useNativeGrpcTransport` in `AppBootstrap`
	- Unity fallback base URL: `http://<host-or-lan-ip>:8080`
	- Endpoints: `POST /session/connect`, `POST /session/heartbeat`
9. Optional: run Envoy gRPC-Web gateway only for explicit experiments:
	- `docker run --rm -it -p 8081:8081 -p 9901:9901 -v "${PWD}/tools/dev/envoy/envoy.yaml:/etc/envoy/envoy.yaml" envoyproxy/envoy:v1.31-latest`
10. Build runtime packages from source fixtures:
	- `python tools/packaging/build_runtime_packages.py --job-id job-mock-001`
11. Trigger runtime package build through HTTP:
	- `POST /api/jobs/{jobId}/packages:build`
	- Response includes `runId` and `statusUrl`.
12. Check package build job status:
	- `GET /api/package-jobs/{runId}`
13. Cancel a queued package build job:
	- `DELETE /api/package-jobs/{runId}`
14. Cleanup expired terminal jobs:
	- `POST /api/package-jobs:cleanup?ttl_seconds=86400`
15. Run dedicated export worker process:
	- `python server-kit/app/export_worker_main.py`

## Next
- Integrate Unity `SessionClient` with gRPC stream + GLB asset stream.
- Add Kit extension bootstrapping and stage-open service.
- Connect export pipeline to Kit USD step resolver output.

## Unity Runtime Notes
- `AppBootstrap` defaults to native gRPC transport (`useNativeGrpcTransport=true`, `grpcTarget=localhost:50051`).
- HTTP bridge remains available as fallback (`useNativeGrpcTransport=false`, `httpBridgeBaseUrl=http://localhost:8080`).
- At runtime, Unity sends periodic heartbeats and attempts reconnect when connection is not in `Connected` state.

## Architecture Decisions (Applied)
- Unity C# protobuf/gRPC generation workflow: `Grpc.Tools` build project under `tools/proto-csharp/`.
- Canonical step-definition source: external YAML (`shared/samples/step-definitions.yaml`).
- Structured logging schema: JSON log lines with fixed fields (`timestamp`, `level`, `event`, `message`, `session_id`, `step_id`, `correlation_id`).
- Runtime glTF loader direction: `glTFast` (Unity integration to follow in client implementation).
- Unity session transport direction for Android 10+ / Unity 6: native direct gRPC as default runtime path (no proxy container required), with HTTP bridge fallback.

## Draco Streaming Policy
- Draco is applied only when both sides support it.
- Client advertises Draco support and validates incoming compression mode.
- Server export pipeline applies Draco only if `GUIDANCE_DRACO_ENABLED=true` and a supported toolchain is available.
- Otherwise server falls back to uncompressed streaming (`ASSET_COMPRESSION_NONE`).

Example environment values:
- `GUIDANCE_DRACO_ENABLED=true`
- `GUIDANCE_DRACO_TOOLCHAIN=gltf-transform`
- `GUIDANCE_DRACO_ENCODER_CMD=<optional_override_command_with_{input}_and_{output}>`
- `GUIDANCE_EXPORT_JOB_STORE_FILE=./server-kit/runtime/export-jobs.json`
- `GUIDANCE_EXPORT_JOB_PROCESSING_MODE=inline` (`enqueue-only` when dedicated worker owns processing)
- `GUIDANCE_EXPORT_JOB_RETENTION_SECONDS=86400`
- `GUIDANCE_EXPORT_WORKER_POLL_SECONDS=1.0`
- `GUIDANCE_SESSION_STORE_FILE=./server-kit/runtime/sessions.json`

When running the dedicated export worker in production-like setups, set `GUIDANCE_EXPORT_JOB_PROCESSING_MODE=enqueue-only` on the HTTP API process so it only enqueues jobs and the worker process is the single processor.
