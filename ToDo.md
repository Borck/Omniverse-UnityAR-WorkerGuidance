# ToDo - Omniverse Unity AR Worker Guidance

Updated: 2026-03-10
Source: `Plan.md` + implemented repository state

## 1. Current Progress (Done)

### M0 Foundation
- [x] Repository and folder skeleton created (`server-kit`, `client-unity`, `proto`, `docs`, `shared`, `tools`)
- [x] Multi-folder VS Code workspace configured (`Omniverse-UnityAR-WorkerGuidance.code-workspace`)
- [x] Base developer setup in place (`.editorconfig`, `.gitignore`, `.env.example`)
- [x] VS Code project configuration created (`.vscode/settings.json`, `tasks.json`, `launch.json`, `extensions.json`)
- [x] Python health endpoint test is running (`server-kit/tests/test_health.py`)

### M1 Shared Contract
- [x] Initial `proto/guidance.proto` created
- [x] Core services defined (`GuidanceSessionService`, `AssetQueryService`)
- [x] Core message envelopes and domain messages defined
- [x] Contract notes documented (`docs/api/proto-contract-notes.md`)
- [x] Python protobuf stubs generated into `server-kit/app/generated/`

### M2/M6 Initial Server Slice
- [x] FastAPI health service implemented (`server-kit/app/server_kit_main.py`)
- [x] Server app refactored to app factory (`create_app()`)
- [x] Environment-driven config loader added (`server-kit/app/config.py`)
- [x] Structured logging adapter with `session_id` and `step_id` added (`server-kit/app/logging_config.py`)
- [x] FastAPI lifecycle migrated to lifespan handlers (`server-kit/app/server_kit_main.py`)
- [x] Mock gRPC `Connect` stream service implemented (`server-kit/app/grpc_session_service.py`)
- [x] `HelloRequest -> HelloResponse` handshake implemented
- [x] Heartbeat -> Ping roundtrip implemented
- [x] Mock `StepActivated` event emission implemented
- [x] gRPC stream behavior verified by automated test (`server-kit/tests/test_grpc_session_stub.py`)
- [x] Conditional Draco negotiation implemented for gRPC GLB streaming with runtime fallback (`server-kit/app/grpc_asset_service.py`)

### M7 Initial Unity Slice
- [x] Unity runtime skeleton in place (`AppBootstrap`, `SessionClient`, `StepCoordinator`)
- [x] Unity-side compression handling path added for streamed assets (`AssetStreamAssembler`, `SessionClient`)

## 2. Active Priorities

### P0 - Immediate (Vertical Slice blockers)
- [ ] Stabilize protobuf generation (Python + C#)
  - [x] Python stub output directory created: `server-kit/app/generated/`
  - [x] C# output directory created: `client-unity/Assets/App/Generated/`
  - [x] README updated with concrete codegen commands and prerequisites
  - [x] C# generation command runs successfully in this workspace (Grpc.Tools workflow)
  - Acceptance criterion: Python and C# stubs are generated from a clean checkout using documented commands.

- [x] Move server bootstrap to modular structure
  - [x] App factory (`create_app()`) implemented
  - [x] Structured logging with `session_id`/`step_id` implemented
  - [x] Config object loaded from environment/settings
  - Acceptance criterion met: server starts reproducibly and logs startup/health events.

- [x] Build mock gRPC session flow
  - [x] Working `Connect` stream server stub implemented
  - [x] `HelloRequest -> HelloResponse` handshake implemented
  - [x] Heartbeat/Ping roundtrip implemented
  - [x] Mock `StepActivated` event sent
  - Acceptance criterion met: test client can connect and receive a step event.

### P1 - Near-term (make vertical slice functional)
- [x] Implement minimal asset manifest path
  - [x] Return static fixture from `GetManifest`
  - [x] Add manifest fixture under `shared/samples/`
  - [x] Define URL conventions for assets/targets
  - Acceptance criterion: Unity client can fetch and parse manifest.

- [x] Prepare HTTP asset endpoints
  - [x] `GET /api/jobs/{jobId}/manifest`
  - [x] `GET /api/assets/{assetVersion}/{fileName}`
  - [x] `GET /api/targets/{targetVersion}/{fileName}`
  - [x] Add ETag/Cache-Control for immutable artifacts
  - Acceptance criterion: versioned assets are retrievable with correct cache headers.

- [ ] Upgrade Unity `SessionClient` from logging stub to real stream client
  - [x] Add connection lifecycle (`Disconnected`, `Connected`, `Faulted`)
  - [x] Model `StepActivated` receive path (DTO + event)
  - [x] Add reconnect error path
  - [x] Add HTTP bridge session endpoints for Unity transport (`POST /session/connect`, `POST /session/heartbeat`)
  - [x] Add runtime heartbeat + periodic reconnect loop in Unity bootstrap
  - Acceptance criterion: client updates local state when mock step event arrives.

- [ ] Implement `StepCoordinator` as a state machine
  - [x] Model base states from plan (`Idle`, `StepReady`, `Tracking`, `Playing`, ...)
  - [x] Add guards for invalid transitions
  - [x] Keep transition logic unit-testable and decoupled from MonoBehaviour
  - Acceptance criterion: transition tests run reliably.

## 3. Remaining Tasks by Milestone

### M2 - Kit Server Skeleton
- [x] Document extension dependency graph (`docs/architecture/`)
- [ ] Add stage-open configuration path (`GUIDANCE_STAGE_URI`)
- [ ] Extend session lifecycle logging
- [ ] Add stage-open smoke test (mock/fixture first)
- Done when: local Kit-oriented server structure runs with health + base config.

### M3 - USD Step Resolver
- [ ] Define `ResolvedStep` data model
- [ ] Define step-definition source (JSON/YAML/USD metadata)
- [ ] Build layer-stack analyzer
- [ ] Implement active prim path resolution
- [ ] Compute deterministic cache key hash
- [ ] Add resolver unit tests with fixed fixtures
- Done when: same inputs always produce same `ResolvedStep` and hash.

### M4 - GLB Export Pipeline
- [x] Implement `ExportCoordinator` scaffold (`StepPackageExporter`)
- [x] Add GLB export adapter (mock-first) with conditional Draco compression via configured toolchain
- [x] Add `step.json` writer according to schema
- [x] Add content hashing and asset versioning
- [x] Add reproducibility test (same input -> same output)
- [x] Wire server-triggered package build endpoint (`POST /api/jobs/{jobId}/packages:build`)
- [x] Persist export job queue/status to disk (`GUIDANCE_EXPORT_JOB_STORE_FILE`)
- [x] Add package job cancellation endpoint (`DELETE /api/package-jobs/{runId}`)
- [x] Add terminal-job TTL cleanup endpoint (`POST /api/package-jobs:cleanup`)
- [x] Add dedicated export worker process (`server-kit/app/export_worker_main.py`)
- [x] Add API processing ownership mode (`GUIDANCE_EXPORT_JOB_PROCESSING_MODE=inline|enqueue-only`)
- Done when: each step produces a versioned reproducible package.

### M5 - Asset Service
- [x] Implement `ManifestService` with immutable index (fixture-based initial version)
- [x] Define filesystem layout for asset cache
- [x] Specify retry-safe/range-safe download behavior
- [x] Expand API docs in `docs/api/`
- Done when: manifest and assets are delivered as immutable versions.

### M6 - gRPC Session Service Hardening
- [x] Add idempotency for duplicate client messages
- [x] Add reconnect/session resume logic
- [x] Carry `correlation_id` end-to-end in fault handling
- [x] Define lightweight persistent session-state storage
- [x] Test stream resilience under temporary network loss
- Done when: short network drops do not lose session state.


### M7 - Unity Client Skeleton Completion
- [ ] Add `AssetCache`, `TargetManager`, `TelemetryClient` skeletons
- [ ] Finalize bootstrapping/DI approach
- [ ] Add placeholder UI for session/step status
- Done when: app boots, connects, and shows mock step status.

### M8 - Runtime GLB Loading
- [ ] Select and integrate runtime glTF loader
- [ ] Implement download -> cache -> load -> instantiate pipeline
- [ ] Enforce one-active-model rule
- [ ] Ensure model dispose/unload on step switch
- [ ] Add long-run memory growth test
- Done when: step transitions replace models cleanly without memory leaks.

### M9 - Vuforia Integration
- [ ] Implement 3DModel-target v1 pipeline
- [ ] Implement target payload cache and per-step activation
- [ ] Define pose smoothing and tracking-loss behavior
- Done when: tracking acquire aligns active model reliably.

### M10 - Step Playback + UX
- [ ] Implement HUD for step name, short instructions, warnings
- [ ] Implement actions `replay`, `next`, `previous`, `confirm`, `help`
- [ ] Support animation replay without reloading assets
- Done when: complete guidance loop is usable on mock workflow.

### M11 - Robustness / Offline
- [ ] Implement reconnect backoff strategy
- [ ] Implement frozen-step mode during network loss
- [ ] Implement active+next prefetch
- [ ] Add local diagnostics bundle export
- Done when: temporary network loss does not break the workflow.

### M12 - Validation / Hardening
- [ ] Build and run integration test matrix
- [ ] Validate performance and thermal budget on target device
- [ ] Validate 3-5 pilot workflows end-to-end
- [ ] Finalize release checklist and failure playbooks
- Done when: pilot-ready release quality is reached.

### M13 - Documentation
- [ ] Add a concept graphic of how this system is working and communicating
- [ ] Add a comprehensive guide of how to
  - [ ] integrate the software in Unity 6
  - [ ] integrate the App into Omniverse
  - [ ] setup everything in between
- [ ] Add a comprehensive documentation to each Kit App class
- [ ] Add a comprehensive documentation to each Unity script/class

## 4. Next 2 Sprint Ticket Cut

### Sprint A - Close technical vertical slice
- [x] T1: Protobuf generation Python/C# + documentation
- [x] T2: gRPC hello/heartbeat server + simple test client
- [x] T3: Manifest fixture + HTTP manifest endpoint
- [x] T4: Unity `SessionClient` handles mock step event
- [ ] T5: `StepCoordinator` base state machine + tests

### Sprint B - First visible end-to-end
- [ ] T6: Runtime GLB loading (local sample)
- [ ] T7: One-active-model lifecycle + unload
- [ ] T8: Image target activation (base)
- [ ] T9: HUD + confirm/replay flow
- [ ] T10: End-to-end demo script documentation

## 5. Open Decisions (Need user input)
- [x] Which C# protobuf/gRPC generation path is official for this repo? (`Grpc.Tools` project workflow)
- [x] Where should canonical step-definition live (USD metadata vs external step file)? (External YAML)
- [x] Which structured logging schema should be standardized? (JSON structured logging)
- [x] Which runtime glTF loader should be standard? (`glTFast`)
- [x] Draco policy: use Draco only when conversion support exists, otherwise fallback to uncompressed transfer (implemented with configurable toolchain and runtime fallback)
- [x] Which Unity mobile transport should be used for Android 10+ / Unity 6? (Native direct gRPC via transport abstraction; HTTP bridge fallback; Envoy optional for gRPC-Web experiments)

## 6. Tracking Rules
- [ ] On each merge: update `Current Progress` and affected milestones.
- [ ] On each new API field: update `docs/api/proto-contract-notes.md`.
- [ ] On each architecture decision: add an ADR under `docs/decisions/`.
