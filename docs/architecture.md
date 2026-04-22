# System Architecture

## 1. Overview

```
┌─────────────────────────┐         gRPC (port 50051)          ┌──────────────────────┐
│  Python Server-Kit      │ ◄──────────────────────────────── │  Unity AR Client     │
│  (FastAPI + gRPC)       │                                    │  (HoloLens / phone)  │
│                         │ ─── HTTP REST (port 8080) ───────► │                      │
│  shared/samples/        │                                    │  persistentDataPath/ │
│    manifests/           │         gRPC (port 50051)          │    guidance-cache/   │
│    assets/              │ ◄──────────────────────────────── │    guidance-target-  │
│    targets/             │                                    │    cache/            │
└─────────────────────────┘                                    └──────────────────────┘
           ▲
           │  (optional: test server replaces Python server)
           │
┌─────────────────────────┐
│  ASP.NET Test Server    │
│  (port 5000)            │
│  - Web Admin UI         │
│  - gRPC session service │
│  - gRPC asset transfer  │
└─────────────────────────┘
```

Both the Python server-kit and the ASP.NET test server implement the same gRPC proto
contract defined in [`proto/guidance.proto`](../proto/guidance.proto). The Unity client
connects to whichever server is running.

---

## 2. Guiding Principle: Zero Embedded Assembly Data

**The Unity AR client ships with no assembly-specific content.**

| What is NOT in the app bundle              | Where it lives instead                              |
|--------------------------------------------|-----------------------------------------------------|
| GLB 3D model files                         | `shared/samples/assets/{version}/` on the server   |
| Vuforia Model Target databases (.dat/.xml) | `shared/samples/targets/{version}/` on the server  |
| Step definitions / instructions            | `shared/samples/step-definitions.yaml` on server   |
| Manifest JSON                              | `shared/samples/manifests/` on the server          |
| Animation clips for assembly steps         | Embedded inside GLB files, served from server      |

Everything is **downloaded at runtime** into `Application.persistentDataPath` and
evicted between sessions on demand. The Editor pre-build check
(`Assets/App/Editor/NoEmbeddedAssemblyDataCheck.cs`) enforces this as a CI gate —
the build will fail if any `.glb`, `.dat`, `.xml`, or `.manifest.json` is detected
inside the project's `Assets/` tree.

---

## 3. Component Map

### Unity AR Client

| Component | File | Responsibility |
|-----------|------|----------------|
| `AppBootstrap` | `Runtime/AppBootstrap.cs` | Main MonoBehaviour: wires all runtime modules, drives the session lifecycle, orchestrates asset resolve → present loop |
| `AppRuntimeContext` | `Runtime/AppRuntimeContext.cs` | Composition root; creates and wires all runtime services |
| `SessionClient` | `Networking/SessionClient.cs` | Thin wrapper over `ISessionTransport`; fires `StepActivated` events |
| `GrpcSessionTransport` | `Networking/GrpcSessionTransport.cs` | Native gRPC duplex stream to server |
| `HttpBridgeSessionTransport` | `Networking/HttpBridgeSessionTransport.cs` | REST fallback when gRPC is not available |
| `GrpcAssetTransferClient` | `Networking/GrpcAssetTransferClient.cs` | Streams GLB or Vuforia target files via `AssetTransferService` gRPC |
| `StepAssetManifestClient` | `Gltf/StepAssetManifestClient.cs` | HTTP GET of step asset manifest from server |
| `AssetCache` | `Caching/AssetCache.cs` | Immutable-by-version disk cache for GLB models |
| `TargetPayloadCache` | `Caching/TargetPayloadCache.cs` | Immutable-by-version disk cache for Vuforia targets |
| `ModelPresenter` | `Gltf/ModelPresenter.cs` | Lifecycle for the active 3D model; dispatches to `IModelLoader` |
| `GltfFastModelLoader` | `Gltf/GltfFastModelLoader.cs` | glTFast-backed async GLB loader (reflection-resolved) |
| `PrimitiveFallbackModelLoader` | `Gltf/PrimitiveFallbackModelLoader.cs` | Cube placeholder when glTFast is absent |
| `TargetManager` | `Vuforia/TargetManager.cs` | Tracks active target metadata and smoothed poses |
| `VuforiaTrackingBridge` | `Vuforia/VuforiaTrackingBridge.cs` | Forwards Vuforia observer events into `AppBootstrap` |
| `VuforiaModelTargetLoader` | `Vuforia/VuforiaModelTargetLoader.cs` | Coroutine-based Vuforia DataSet activation from runtime-downloaded file |
| `StepCoordinator` | `StateMachine/StepCoordinator.cs` | Step state machine (Idle → Tracking → Confirmed) |
| `TelemetryClient` | `Telemetry/TelemetryClient.cs` | Fault/event tracking |

### Python Server-Kit

| Component | File | Responsibility |
|-----------|------|----------------|
| FastAPI app | `server_kit_main.py` | HTTP REST endpoints: manifest, assets, targets, session bridge |
| gRPC session service | `grpc_session_service.py` | Duplex stream: hello/heartbeat/step-completed → step-activated |
| gRPC asset service | `grpc_asset_service.py` | `StreamStepAsset`: GLB or Vuforia target streaming |
| `ManifestService` | `manifest_service.py` | Parses `{job_id}.manifest.json` from disk |
| `StepDefinitionRepository` | `step_definition_repository.py` | Reads `step-definitions.yaml` |
| `DracoCodec` | `draco_codec.py` | Optional Draco mesh compression for GLB chunks |

### ASP.NET Test Server

| Component | File | Responsibility |
|-----------|------|----------------|
| `Program.cs` | `test-server/Program.cs` | Kestrel + gRPC + Razor Pages wiring |
| `GuidanceSessionServiceImpl` | `Services/GuidanceSessionServiceImpl.cs` | gRPC duplex session |
| `AssetTransferServiceImpl` | `Services/AssetTransferServiceImpl.cs` | gRPC asset chunk streaming |
| `JobStore` | `Storage/JobStore.cs` | In-memory job store; notifies waiting gRPC streams on job activation |
| `FileAssetStore` | `Storage/FileAssetStore.cs` | Saves uploaded files to `data/` directories |
| `Index` page | `Pages/Index.cshtml` | Job list + "Notify Unity" button |
| `Jobs/Submit` page | `Pages/Jobs/Submit.cshtml` | Multi-step job creation form with file uploads |

---

## 4. Runtime Data Flow

```
Operator (web browser)
  │
  ▼
test-server /Jobs/Submit (POST)
  │  ① Upload GLB + Vuforia target files
  │  ② Write manifest JSON
  │  ③ Insert into JobStore
  │  ④ JobStore.SetActiveJob() → notifies waiting gRPC streams
  ▼
GuidanceSessionServiceImpl.Connect() (gRPC server stream)
  │  ⑤ Sends StepActivated { job_id, step_id, asset_version, target_version, … }
  ▼
Unity AR Client — GrpcSessionTransport.ReadLoopAsync()
  │  ⑥ Fires SessionClient.StepActivated event
  ▼
AppBootstrap.OnSessionStepActivated()
  │  ⑦ Starts coroutine ResolveAndPresentStepAsset()
  │
  ├─ ⑧  HTTP GET /api/jobs/{job_id}/manifest  →  StepAssetManifestClient
  ├─ ⑨  gRPC StreamStepAsset(ASSET_TYPE_GLB)  →  GrpcAssetTransferClient  →  AssetCache
  ├─ ⑩  gRPC StreamStepAsset(ASSET_TYPE_VUFORIA_TARGET)  →  GrpcAssetTransferClient  →  TargetPayloadCache
  │
  ├─ ⑪  ModelPresenter.PresentModelAsync()  →  GltfFastModelLoader.LoadModelAsync()
  └─ ⑫  TargetManager.ActivateTarget()  →  VuforiaModelTargetLoader (if VUFORIA_ENGINE)
```

---

## 5. Proto-Defined Service Contracts

See [`proto/guidance.proto`](../proto/guidance.proto) for the canonical contract.
See [`docs/proto-reference.md`](./proto-reference.md) for field-level documentation.
