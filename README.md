# Omniverse Unity AR Worker Guidance

## Core Features
- Cross-runtime architecture with an Omniverse server and a Unity 6 AR client.
- Step-driven worker guidance with deterministic progression (`confirm -> next step`) and reconnect-safe session flow.
- BTU-style layer handover model (`target-position` override plus next animation activation) with deterministic resolver output.
- Dual transport support for Unity session connectivity: native gRPC by default plus HTTP bridge fallback.
- Runtime asset delivery pipeline with immutable manifests, versioned artifacts, local caching, and one-active-model lifecycle.
- Export pipeline with reproducible package generation, queue/worker processing mode, cancel/cleanup endpoints, and job status tracking.
- Structured observability with JSON logging, diagnostics export, and validation artifacts for pilot readiness.

This repository contains the first implementation slice for an AR worker guidance system.

## Components
- `server-kit/`: Omniverse Kit server for step resolution, export, gRPC, and HTTP services.
- `client-unity/`: Unity 6 client runtime for session orchestration, model loading, and tracking.
- `proto/`: Shared protobuf contracts used by server and client.
- `shared/`: Shared schemas, sample payloads, and fixtures.

## Omniverse Assembly Scene (Reference Example)
The current section documents an example layer-stack pattern to demonstrate runtime behavior.
Part names and timing windows shown below are examples, not hard-coded product constraints.

Timeline:
- time steps `1-101`
- `30 FPS`

Per part, two layers exist:
1. animation layer
2. target-position layer

Movement contract for each animated part:
- start offset: `(0, 0.1, 0)`
- end position: `(0, 0, 0)`
- after the animation window, the part remains visible to step `101`

Example layer order:
- Layer 1 animation: `PART_A` (`1-10`)
- Layer 2 target-position: `PART_A`
- Layer 3 animation: `PART_B` (`11-30`)
- Layer 4 target-position: `PART_B`
- Layer 5 animation: `PART_C` (`31-40`)
- Layer 6 target-position: `PART_C`
- Layer 7 animation: `PART_D` (`41-50`)
- Layer 8 target-position: `PART_D`
- Layer 9 animation: `PART_E` (`51-60`)
- Layer 10 target-position: `PART_E`

Runtime control logic:
- Start with only the first animation layer active.
- On placement confirmation for current part:
	- unmute target-position layer for that part
	- target-position layer overrides animated visualization
	- keep placed part fixed at final position
	- activate next animation layer automatically
- Repeat until all parts are completed.

Implementation note:
- The real scene should be read from the configured step-definition/layer metadata source, not inferred from these example identifiers.
- The repository includes this reference fixture in `shared/samples/step-definitions.yaml` under job ID `job-layer-example-001`.

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
- Native gRPC session transport now sends `step_completed` for sequence-driven step progression.
- HTTP bridge transport now also sends `step_completed` to `/session/step-completed` and can receive next `step_activated` payloads.
- Server-side `LayerStackResolver` now mirrors BTU `switch_step_layers_ui` behavior for pairwise layer muteness (`completed end-position layers + next animation layer`) and deterministic cache-key generation.
- Export pipeline now supports a pluggable GLB exporter backend (`PassthroughGlbExporter` default) and includes an Omniverse-adapted stage exporter (`OmniverseStageGlbExporter`) modeled after the tested extension flow (flatten stage + asset converter settings).
- Unity runtime includes skeleton modules for `AssetCache`, `TargetManager`, and `TelemetryClient`.
- Runtime asset path now resolves manifest step entry, caches GLB locally, and enforces one-active-model lifecycle via presenter.

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
16. Run mocked stage-open smoke check (no Kit runtime required):
	- `POST /api/stage:open-smoke`
	- Uses `GUIDANCE_STAGE_URI` and validates URI contract/scheme.
17. Preview BTU-style layer muteness resolution for a job:
	- `POST /api/jobs/{jobId}/layers:resolve`
	- Body: `{"sublayer_paths_bottom_to_top": ["...", "..."]}`
	- Returns per-step visible/muted layer sets and deterministic cache keys.
18. Run automated M12 validation matrix (backend checks):
	- `pwsh tools/scripts/run-validation-matrix.ps1`

## Validation And Deployment Docs
- Integration matrix: `docs/validation/integration-test-matrix.md`
- Performance and thermal budget: `docs/validation/performance-thermal-budget.md`
- Pilot E2E workflows: `docs/validation/pilot-workflows-e2e.md`
- Pilot run sheet template: `docs/validation/pilot-run-sheet-template.md`
- Pilot evidence structure: `docs/validation/evidence-folder-structure.md`
- Release checklist and failure playbooks: `docs/validation/release-checklist-and-playbooks.md`

## Integration Guides
- System concept graphic: `docs/architecture/system-concept-graphic.md`
- Unity 6 integration: `docs/guides/unity6-integration-guide.md`
- Omniverse integration: `docs/guides/omniverse-integration-guide.md`
- End-to-end setup: `docs/guides/end-to-end-setup-guide.md`

## Next
- Integrate Unity `SessionClient` with gRPC stream + GLB asset stream.
- Add Kit extension bootstrapping and stage-open service.
- Connect export pipeline to Kit USD step resolver output.
- Implement deterministic layer-pair handover in runtime (`confirm -> target-position override -> next animation`).

## Unity Runtime Notes
- `AppBootstrap` defaults to native gRPC transport (`useNativeGrpcTransport=true`, `grpcTarget=localhost:50051`).
- HTTP bridge remains available as fallback (`useNativeGrpcTransport=false`, `httpBridgeBaseUrl=http://localhost:8080`).
- At runtime, Unity sends periodic heartbeats and attempts reconnect when connection is not in `Connected` state.
- Step activation triggers manifest lookup and local cached GLB resolution before presenting a single active model.

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
- `GUIDANCE_STAGE_URI=omniverse://localhost/Projects/Assembly.usd`

When running the dedicated export worker in production-like setups, set `GUIDANCE_EXPORT_JOB_PROCESSING_MODE=enqueue-only` on the HTTP API process so it only enqueues jobs and the worker process is the single processor.
