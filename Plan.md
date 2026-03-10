# Development Plan

## Project Goal
Build a production-oriented AR guidance system where Omniverse is the scene and process authority, a Kit-based server derives step-specific runtime payloads from a USD scene, and a Unity 6 client on Vuzix renders exactly one active part with its animation and Vuforia-based alignment.

The Omniverse scene currently follows a layered USD structure:
- One layer contains the full hierarchical master geometry.
- Additional layers add animations and visibility changes that are applied to the master geometry.

## Omniverse Assembly Scene Profile
This plan includes a reference assembly scene profile to illustrate layer-stack behavior.
Part identifiers and timing windows in this section are examples.

### Timeline baseline
- Playback range: time steps `1-101`
- Frame rate: `30 FPS`

### Layer pair contract
Each assembly part is represented by two layers:
1. animation layer
2. target-position layer

Per-part movement contract:
- start offset: `(0, 0.1, 0)`
- final target position: `(0, 0, 0)`
- animated layer becomes static-visible until step `101`
- separate target-position layer exists at `(0, 0, 0)`

### Example layer sequence
- Layer 1: animation `PLATE_BOTTOM_01_001`, active `1-10`, static-visible until `101`
- Layer 2: target-position `PLATE_BOTTOM_01_001`, static `(0,0,0)`
- Layer 3: animation `CORE_ROW_00_002`, active `11-30`, static-visible until `101`
- Layer 4: target-position `CORE_ROW_00_002`, static `(0,0,0)`
- Layer 5: animation `LEFT_UNIT_PHASE_03_001`, active `31-40`, static-visible until `101`
- Layer 6: target-position `LEFT_UNIT_PHASE_03_001`, static `(0,0,0)`
- Layer 7: animation `RIGHT_UNIT_PHASE_03_001`, active `41-50`, static-visible until `101`
- Layer 8: target-position `RIGHT_UNIT_PHASE_03_001`, static `(0,0,0)`
- Layer 9: animation `PLATE_TOP_02_002`, active `51-60`, static-visible until `101`
- Layer 10: target-position `PLATE_TOP_02_002`, static `(0,0,0)`

The runtime/resolver must treat this list as an example fixture. Actual production sequences are resolved from configured scene metadata.

### Runtime handover logic
- Start with only the first animation layer active.
- On part placement confirmation:
  - unmute the corresponding target-position layer
  - treat target-position layer as visual override of the animated layer
  - keep placed part fixed at target position
  - automatically activate next animation layer
- Repeat for all parts.

This plan is optimized for implementation in a single VS Code workspace with GitHub Copilot Agent support.

## Product Scope
### In scope
- Omniverse Kit server project.
- Unity 6 client project.
- Shared protobuf contract.
- gRPC control channel.
- Asset delivery for GLB and Vuforia target payloads.
- Runtime playback of one active part at a time.
- Step-based worker self-guidance workflow.
- Local caching, telemetry, and failure handling.
- Local development environment and CI-ready repository structure.

### Out of scope for v1
- Full runtime OpenUSD import in Unity.
- Multi-part simultaneous visualization.
- Multi-user collaboration on-device.
- Authoring UI inside Unity.
- Complex cloud deployment.
- ERP/MES integration beyond placeholder adapters.

## Target Architecture
- `target/` payload for Vuforia
- optional `thumbnail.png`

- job assignment
- step activation
### Data plane
Use HTTP file serving or signed local URLs for:
- GLB asset download
- Vuforia target download
- Omniverse remains the source of truth.
- Unity runtime stays lightweight.
- Only one part is loaded and rendered at a time.
- The control channel stays small and deterministic.
- Asset caching becomes simple and robust.

## USD Interpretation Strategy
### Source scene model
Assume the USD stage is structured like this:
- Base geometry layer: authoritative complete hierarchical product geometry.
- Animation layers: add time-sampled transforms or animation clips.
- Visibility layers: turn subcomponents on or off for a specific assembly state.

- `stepId`
- `workflowVersion`
- `partId`
- `displayName`
- `sourcePrimPath`
- `animationName`
- `anchorType`
- `targetId`
- `assetVersion`
- `instructionsShort`
- `safetyNotes`
- `expectedDurationSec`

## Repository and Workspace Layout
Create one root repository with a multi-folder VS Code workspace.

```text
ar-worker-guidance/
  .github/
    workflows/
  .vscode/
    settings.json
    tasks.json
    launch.json
    extensions.json
  docs/
    architecture/
    api/
    decisions/
  proto/
    guidance.proto
  tools/
    scripts/
    dev/
    packaging/
  server-kit/
    app/
    exts/
      company.guidance.core/
      company.guidance.export/
      company.guidance.grpc/
      company.guidance.http/
      company.guidance.tests/
    config/
    tests/
  client-unity/
    Assets/
      App/
        Runtime/
        Editor/
        Networking/
        StateMachine/
        Vuforia/
        Gltf/
        Caching/
        Telemetry/
        UI/
      StreamingAssets/
      Resources/
    Packages/
    ProjectSettings/
  shared/
    schemas/

## VS Code Workspace Setup
- `proto`
- `docs`
- GitHub Copilot Chat
- C# Dev Kit
- ms-python.python
- protobuf support extension
- EditorConfig
- Use tasks for common workflows.
- Use a single root `.env.example` for local endpoints.
Goal: create repository, workspace, standards, and working local builds.

Deliverables:
- workspace skeleton
- README with setup steps
- coding conventions
- formatting and linting config
- protobuf code generation task
- local run tasks for server and Unity

Acceptance criteria:
- repository opens cleanly in VS Code
- Copilot Agent can navigate the workspace
- protobuf codegen runs from one command
- both projects build independently

## M1 - Shared Contract
Goal: define the protocol first.

Deliverables:
- `guidance.proto`
- versioning rules
- error model
- session model
- asset manifest model
- telemetry events

Core messages:
- `HelloRequest`
- `HelloResponse`
- `AssignJob`
- `StepActivated`
- `StepCompleted`
- `TrackingState`
- `UserAction`
- `AssetManifest`
- `Fault`
- `Heartbeat`

Acceptance criteria:
- server and client compile against generated stubs
- sample JSON equivalents are documented
- backward compatibility strategy documented

## M2 - Kit Server Skeleton
Goal: build a minimal runnable Kit app with modular extensions.

Deliverables:
- Kit app entry point
- extension dependency graph
- config files
- logging setup
- health endpoint
- test scene loading

Suggested extension split:
- `company.guidance.core`: domain models and orchestration
- `company.guidance.export`: USD evaluation and GLB export
- `company.guidance.grpc`: gRPC host and session logic
- `company.guidance.http`: static asset serving and manifests
- `company.guidance.tests`: integration tests and fixtures

Acceptance criteria:
- Kit app starts locally
- stage can be opened from configurable path or Nucleus URL
- server logs session lifecycle
- health endpoint responds

## M3 - USD Step Resolver
Goal: convert the layered USD scene into a deterministic step model.

Tasks:
- define a step configuration source for layer pairs (`animation`, `target-position`)
- map USD layers to part semantics and sequence order
- resolve active prim path for a step
- compute visibility set for the step
- compute animation layer stack and time window for each step
- produce a canonical internal `ResolvedStep`
- encode transition policy: `confirm -> target-position unmute -> next animation active`

Implementation notes:
- treat the full geometry layer as immutable source geometry
- treat animation and visibility layers as overlays
- keep export resolution deterministic by sorting layers and applying explicit step config
- persist a hash of all inputs for cache invalidation

Acceptance criteria:
- given a sample scene, the resolver returns the same result on repeated runs
- the resolver identifies the correct prim subtree and animation source
- resolver returns expected windows for the active configured profile (example fixture windows: `1-10`, `11-30`, `31-40`, `41-50`, `51-60`)
- cache key changes when source layers change

## M4 - GLB Export Pipeline
Goal: export one runtime-ready part package per active step.

Tasks:
- isolate active part geometry from the composed stage
- bake transform state as needed
- export animation in a Unity-friendly form
- package textures with the asset
- write `step.json`
- compute content hashes

Output contract:
```json
{
  "stepId": "17",
  "partId": "Bracket_12",
  "assetVersion": "sha256:...",
  "glb": "part_Bracket_12_<hash>.glb",
  "animationName": "Insert_17",
  "targetId": "AssemblyMarker_A",
  "targetVersion": "2026-03-10.1",
  "anchorType": "ImageTarget"
}
```

Acceptance criteria:
- output package is reproducible
- Unity can load the GLB at runtime
- only one part is included in the package
- package version changes only when content changes

## M5 - Asset Service
Goal: expose step packages to devices.

Tasks:
- implement HTTP asset serving
- support cache headers and ETags
- provide manifest endpoint
- optional signed URLs for future remote deployment
- keep local disk cache structure predictable

Suggested endpoints:
- `GET /health`
- `GET /api/jobs/{jobId}/manifest`
- `GET /api/assets/{assetVersion}/{fileName}`
- `GET /api/targets/{targetVersion}/{fileName}`

Acceptance criteria:
- Unity downloads assets with resumable or retry-safe behavior
- cache validation works via ETag or version key
- manifest always points to immutable asset versions

## M6 - gRPC Session Service
Goal: establish reliable orchestration between Omniverse and the device.

Tasks:
- implement bidirectional streaming session
- track device registration and capabilities
- assign workflow and step states
- handle confirmations, retries, and faults
- persist lightweight session state

State machine:
- `Disconnected`
- `Connected`
- `Idle`
- `JobAssigned`
- `StepReady`
- `Tracking`
- `Playing`
- `AwaitingConfirmation`
- `Completed`
- `Faulted`

Acceptance criteria:
- device reconnect restores the last known job and step
- duplicate messages are idempotent
- the session stream survives temporary network loss

## M7 - Unity 6 Client Skeleton
Goal: create a clean runtime architecture before feature work.

Runtime modules:
- `SessionClient`
- `AssetCache`
- `StepCoordinator`
- `TargetManager`
- `ModelPresenter`
- `AnimationPresenter`
- `TrackingPresenter`
- `ConfirmationController`
- `TelemetryClient`

Scene setup:
- bootstrap scene only
- persistent app root
- additive feature scenes only if needed
- one active part root at runtime

Acceptance criteria:
- app boots on device
- connects to server
- receives heartbeat and mock step event
- displays placeholder content

## M8 - Runtime GLB Loading
Goal: load and dispose one part safely and quickly.

Tasks:
- integrate glTF runtime loader
- support download to cache then local load
- instantiate under a controlled root transform
- release old part before showing new part
- preload next step asset without display

Implementation rules:
- only one active model instance
- strict lifecycle: download -> validate -> load -> instantiate -> show -> dispose previous
- keep a fallback placeholder for load failure

Acceptance criteria:
- step transition replaces the model cleanly
- memory does not grow unbounded across repeated steps
- animation clip can be found and played after load

## M9 - Vuforia Integration
Goal: align runtime content to the workstation or assembly.

Tasks:
- choose primary target type for v1
- load target data from local cache
- activate target observer for the current step only
- align part root to target pose
- smooth pose updates and tracking loss handling

Recommended v1 choice:
- start with Image Targets for deterministic deployment and easier iteration
- add Model Targets later if real geometry recognition is required

Acceptance criteria:
- target package can be updated independently from GLB
- tracking acquired event aligns the active part
- tracking loss triggers graceful degradation instead of hard reset

## M10 - Step Playback and Guidance UX
Goal: deliver an operator-safe guidance loop.

Tasks:
- display one step at a time
- show part name and short instruction
- animate insertion path locally
- apply layer-pair handover after confirmation (target-position override)
- support replay animation
- support next, previous, confirm, and help actions
- support visual states: idle, tracking, active, warning, blocked

Acceptance criteria:
- worker can complete a full mocked workflow hands-free or with minimal input
- animation replay does not require asset reload
- UI remains readable on the target Vuzix device
- confirmation of a part fixes it at target-position and activates the next animation step automatically

## M11 - Robustness and Offline Behavior
Goal: tolerate real shopfloor conditions.

Tasks:
- implement reconnect logic
- cache active and next-step assets
- keep last valid target package
- add timeout handling
- implement frozen-step mode during network loss
- record local diagnostics for support

Acceptance criteria:
- temporary Wi-Fi loss does not crash the client
- current step remains usable with cached data
- reconnect resynchronizes with server state safely

## M12 - Validation and Hardening
Goal: prepare for pilot deployment.

Tasks:
- integration test matrix
- performance budget validation
- battery and thermal checks on target device
- operator trial with 3 to 5 realistic workflows
- logging review and privacy review
- deployment packaging

Acceptance criteria:
- pilot scenarios execute end to end
- known failure modes are documented with mitigation
- release checklist passes

## Detailed Backlog
### Server backlog
- create Kit app launcher
- create extension templates
- implement config loader
- implement USD stage opener
- implement layer stack inspector
- implement step resolver service
- implement export job queue
- implement GLB export adapter
- implement manifest writer
- implement HTTP file server
- implement gRPC session service
- implement session persistence
- implement telemetry sink
- implement structured logging
- implement integration tests with sample USD scenes

### Unity backlog
- create bootstrap scene
- create app root prefab
- implement device config loader
- implement gRPC client wrapper
- implement asset downloader
- implement cache index
- integrate runtime GLB loader
- implement target cache and activation
- implement step state machine
- implement animation playback wrapper
- implement UX canvas and status indicators
- implement telemetry upload
- implement diagnostics export
- implement device performance overlay for development

### Shared backlog
- define protobuf messages
- define manifest schema
- define error codes
- define semantic versioning rules
- create sample fixtures
- document API contracts

## Suggested Technical Decisions
### Server language
Prefer Python for the first Kit server iteration to maximize velocity inside Omniverse Kit.

### Unity language
Use C# only, with clear assembly definition boundaries.

### Asset format
Use GLB for runtime geometry and animation payloads.

### Control format
Use gRPC streaming for orchestration and device events.

### Asset transport
Use HTTP for binary payload delivery.

### Target strategy
Start with Image Targets unless there is a strong reason to begin with Model Targets.

### Caching
Cache by immutable content hash, not by mutable logical step name.

### Observability
Use structured logs with correlation IDs per session and per step.

## Protobuf Plan
Create `proto/guidance.proto` with the following service split:

```proto
service GuidanceSessionService {
  rpc Connect(stream ClientMessage) returns (stream ServerMessage);
}

service AssetQueryService {
  rpc GetManifest(ManifestRequest) returns (AssetManifest);
}
```

Key envelopes:
- `ClientMessage` oneof: hello, heartbeat, trackingState, userAction, stepCompleted, fault
- `ServerMessage` oneof: helloResponse, assignJob, stepActivated, cancelStep, ping, fault

## Step Metadata Schema
Create a `step.json` schema like this:

```json
{
  "jobId": "job-001",
  "workflowVersion": "1.0.0",
  "stepId": "17",
  "partId": "Bracket_12",
  "displayName": "Install bracket",
  "instructionsShort": "Align the bracket and insert along the highlighted path.",
  "safetyNotes": ["Verify cable clearance before insertion."],
  "assetVersion": "sha256:...",
  "targetId": "AssemblyMarker_A",
  "targetVersion": "2026-03-10.1",
  "anchorType": "ImageTarget",
  "animationName": "Insert_17",
  "prefetchNextStepId": "18"
}
```

## Unity Runtime Class Map
Suggested class ownership:
- `AppBootstrap`: startup and dependency graph
- `SessionClient`: gRPC streaming client
- `AssetManifestService`: resolve current asset URLs
- `AssetCache`: download and local storage
- `StepCoordinator`: central state machine
- `StepPackageLoader`: load `step.json`, GLB, target payload
- `RuntimeModelLoader`: wraps glTF runtime import
- `RuntimeAnimationController`: finds and plays imported animation
- `TargetManager`: Vuforia observer lifecycle
- `GuidanceHudController`: text and state UI
- `UserActionController`: confirm, replay, help
- `DiagnosticsController`: local troubleshooting bundle

## Kit Server Class Map
Suggested service ownership:
- `StageRepository`: open and watch stage
- `LayerStackAnalyzer`: inspect geometry, animation, and visibility layers
- `StepDefinitionRepository`: maps business steps to USD composition inputs
- `ResolvedStepBuilder`: compose final step state
- `ExportCoordinator`: queue and execute exports
- `GlbPackageWriter`: output package files
- `ManifestService`: immutable asset indexing
- `SessionManager`: device session lifecycle
- `CommandDispatcher`: send step activation
- `TelemetryRecorder`: persist runtime feedback

## Development Workflow for Copilot Agent
### Rules for autonomous implementation
- Always implement against explicit interfaces first.
- Keep each extension and runtime module small and testable.
- Do not allow direct cross-folder imports without an interface boundary.
- Generate code from protobuf, never hand-maintain generated stubs.
- Prefer thin adapters over framework-heavy abstractions.
- Add tests for each state machine transition.
- Log every externally visible state change.

### Copilot-friendly task slicing
Break implementation into tickets of 1 to 3 files where possible.

Suggested ticket order:
1. repository skeleton
2. workspace files
3. protobuf contract
4. Kit app bootstrap
5. gRPC server shell
6. Unity gRPC client shell
7. manifest schema
8. asset cache
9. step state machine
10. runtime GLB loading
11. Vuforia target activation
12. full integration path

## Testing Strategy
### Automated tests
- protobuf compatibility tests
- step resolver unit tests
- export packaging tests
- HTTP manifest tests
- gRPC session tests
- Unity play mode tests for state machine and asset lifecycle

### Manual tests
- mock workflow on desktop Unity
- on-device tracking test
- repeated reconnect test
- repeated step replay test
- corrupted asset recovery test

### Golden test data
Create a minimal USD sample set with:
- one master geometry layer
- two animation overlay layers
- two visibility overlay layers
- three logical steps

## Definition of Done
A milestone is done only if:
- code builds locally
- tests pass
- logs are understandable
- configuration is documented
- no TODO placeholders remain in public interfaces
- one markdown note explains how the feature works

## Risks and Mitigations
### Risk: export fidelity mismatch between USD and GLB
Mitigation: constrain v1 content rules and create export validation scenes.

### Risk: tracking instability on the shopfloor
Mitigation: keep one active target, use frozen-step fallback, and test marker placement early.

### Risk: network instability
Mitigation: cache active and next assets, make gRPC messages idempotent, and keep local fallback state.

### Risk: device thermal or battery limits
Mitigation: enforce one active part, limit animation complexity, and add performance telemetry.

### Risk: uncontrolled scene conventions
Mitigation: define step authoring conventions and validate layer composition in the server.

## Immediate Next Actions
1. Create the repository and workspace skeleton.
2. Write `guidance.proto` before any feature code.
3. Create a minimal Kit app that opens the stage and exposes `/health`.
4. Create a minimal Unity app that connects and displays mock step text.
5. Add GLB runtime loading with a single local sample asset.
6. Add Vuforia target loading for one sample marker.
7. Integrate the full step activation path end to end.

## First Sprint Proposal
### Sprint goal
Achieve an end-to-end vertical slice where Omniverse activates one mocked step and Unity loads one GLB, aligns it to one marker, and plays one animation.

### Sprint backlog
- repository skeleton
- workspace configuration
- protobuf contract draft
- Kit server bootstrap
- health endpoint
- mock gRPC step event
- Unity app bootstrap
- gRPC connection
- local GLB runtime load
- one sample Vuforia target
- one end-to-end demo script

### Sprint exit criteria
- demo starts from a clean checkout
- one command starts the Kit server
- Unity connects and receives a mocked step
- one asset appears on target and plays animation
- logs are sufficient to debug the full path
