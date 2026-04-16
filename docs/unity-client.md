# Unity Client Guide

## Project Dependencies

| Package | Source | Purpose |
|---------|--------|---------|
| **glTFast** | Unity Package Manager | Async GLB model loading at runtime |
| **Vuforia Engine** | Vuforia Developer Portal | Model target tracking |
| **gRPC C#** (`Grpc.Core`) | NuGet / Unity package | gRPC duplex session + asset streaming |
| **Google.Protobuf** | NuGet / Unity package | Protobuf serialization |

The generated C# stubs (`Guidance.cs`, `GuidanceGrpc.cs`) in
`client-unity/Assets/App/Generated/` are re-generated automatically from
`proto/guidance.proto` by building `tools/proto-csharp/ProtoCSharpGen.csproj`.

---

## Zero-Data Build Guarantee

The Unity project contains **no assembly-specific assets**. The following must never
be added to `Assets/` or `StreamingAssets/`:

- `.glb` / `.gltf` files for assembly parts
- Vuforia model target databases (`.dat` / `.xml`)
- Manifest JSON or step JSON files
- Animation clips created for specific assembly steps

The pre-build guard (`Assets/App/Editor/NoEmbeddedAssemblyDataCheck.cs`) enforces this
at every build. If a forbidden file is detected the build will be aborted with an error.

---

## Key MonoBehaviours

### `AppBootstrap`

The main orchestrator. Attach to a root GameObject in your scene.

**Inspector Fields:**

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `useNativeGrpcTransport` | `bool` | `true` | Use gRPC (`true`) or HTTP bridge (`false`) |
| `grpcTarget` | `string` | `localhost:50051` | gRPC server address (host:port) |
| `httpBridgeBaseUrl` | `string` | `http://localhost:8080` | HTTP server base URL |
| `enableRuntimeAssetPipeline` | `bool` | `true` | Download and present assets on step activation |
| `autoConfirmStepAfterAssetReady` | `bool` | `false` | Auto-confirm step after asset loads |
| `autoConfirmDelaySeconds` | `float` | `0.5` | Delay before auto-confirm |
| `heartbeatIntervalSeconds` | `float` | `5` | Seconds between heartbeat messages |
| `reconnectMinIntervalSeconds` | `float` | `2` | Minimum reconnect retry interval |
| `reconnectMaxIntervalSeconds` | `float` | `20` | Maximum reconnect retry interval |
| `statusPanel` | `SessionStatusPanel` | — | Optional HUD panel MonoBehaviour |
| `trackingDirectionHint` | `TrackingDirectionHint` | — | Optional direction hint arrow |

**Public Methods:**

| Method | Description |
|--------|-------------|
| `ConfirmActiveStep()` | Confirm step completion; sends `StepCompleted` to server |
| `ReplayActiveStep()` | Re-present the current step assets |
| `PreviousStep()` | (placeholder) Navigate to previous step |
| `ShowHelp()` | Display help overlay |
| `ExportDiagnosticsBundle()` | Write diagnostics JSON to `persistentDataPath` |
| `OnTargetTrackingUpdated(pos, rot, acquired)` | Called by `VuforiaTrackingBridge` on tracking change |

### `VuforiaTrackingBridge`

Attach to a Vuforia `ObserverEventHandler` GameObject. Forwards tracking events to
`AppBootstrap.OnTargetTrackingUpdated`.

---

## Runtime Asset Flow

When a `StepActivated` message is received:

1. `StepAssetManifestClient` fetches the manifest via HTTP to get URLs and version keys.
2. **GLB model**: downloaded via gRPC `StreamStepAsset(ASSET_TYPE_GLB)` or HTTP, cached
   in `Application.persistentDataPath/guidance-cache/{assetVersion}/`.
3. **Vuforia target**: downloaded via gRPC `StreamStepAsset(ASSET_TYPE_VUFORIA_TARGET)` or
   HTTP, cached in `Application.persistentDataPath/guidance-target-cache/{targetVersion}/`.
4. `ModelPresenter.PresentModelAsync()` asynchronously loads the GLB using glTFast.
5. `TargetManager.ActivateTarget()` registers the target with Vuforia
   (via `VuforiaModelTargetLoader` if Vuforia Engine is present).

All downloads are **version-keyed** — if the file is already cached for the current
version, no network request is made.

---

## Changing the Server Address

1. Select the `AppBootstrap` GameObject in the scene hierarchy.
2. In the Inspector, update **gRPC Target** (e.g. `192.168.1.100:5000` for the test server)
   and/or **Http Bridge Base Url** (e.g. `http://192.168.1.100:8080` for the Python server).
3. Save the scene.

---

## Build Settings

- **Platform**: Android (HoloLens / Quest) or iOS
- **Scripting Backend**: IL2CPP
- **API Compatibility Level**: .NET Standard 2.1 or .NET Framework 4.x
- The `#if VUFORIA_ENGINE` guards in `VuforiaModelTargetLoader.cs` activate automatically
  when the Vuforia Engine package is imported.
- The `#if UNITY_EDITOR` guards in any test stubs ensure mock data never reaches a build.
