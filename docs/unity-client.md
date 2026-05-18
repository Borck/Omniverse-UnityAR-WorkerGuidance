# Unity Client Guide

## Project Dependencies

| Package | Source | Purpose |
|---------|--------|---------|
| **glTFast** (`com.atteneder.gltfast`) | UPM (git URL) | Async GLB model loading at runtime, with animation support |
| **YetAnotherHttpHandler** (`com.cysharp.yetanotherhttphandler`) | UPM (git URL) | Rust-based HTTP/2 client; required for `Grpc.Net.Client` to work on Unity Android IL2CPP |
| **Vuforia Engine** (`com.ptc.vuforia.engine` 11.4.4) | UPM (`.tgz`) | Model target tracking |
| **Grpc.Net.Client** + **Grpc.Net.Common** + **Grpc.Core.Api** (2.76.x) | NuGetForUnity | Pure-managed gRPC client (replaces deprecated `Grpc.Core` C-core library) |
| **Google.Protobuf** (3.34.x) | NuGetForUnity | Protobuf serialization |
| **System.IO.Pipelines** (6.x) | NuGetForUnity | Required by YetAnotherHttpHandler |

The generated C# stubs (`Guidance.cs`, `GuidanceGrpc.cs`) in
`client-unity/Assets/App/Generated/` are re-generated automatically from
`proto/guidance.proto` by building `tools/proto-csharp/ProtoCSharpGen.csproj`.

---

## Why YetAnotherHttpHandler?

`Grpc.Net.Client` needs an HTTP/2 transport. Unity's bundled .NET runtime does not
expose `System.Net.Http.SocketsHttpHandler` (it is .NET 5+ only, not in .NET Standard 2.1
or Unity's Mono profile). YetAnotherHttpHandler is a Rust-based replacement designed
specifically for this exact scenario; without it the gRPC stream silently stalls on
Android IL2CPP after the TCP connection succeeds.

The `link.xml` at `Assets/link.xml` preserves `Grpc.Net.Client`, `Grpc.Net.Common`,
`Grpc.Core.Api`, `Google.Protobuf`, `System.Net.Http`, `System.Net.Sockets`, `glTFast`,
and `glTFast.Newtonsoft` from IL2CPP managed-code stripping.

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

**Whitelisted exceptions** (allowed even though they match the patterns):
- `Assets/link.xml` — IL2CPP linker preservation file (a `.xml`, but it's a Unity build-system file, not assembly data)
- `Assets/Packages/**` — NuGet package output (e.g. `System.IO.Pipelines.xml` doc file)
- `Assets/Plugins/Android/**` — Android platform configuration (`AndroidManifest.xml`, `network_security_config.xml`)
- `Assets/App/Tests/**` — editor-only test fixtures

The fixture overlay model (`Half_Fixture.obj`) is allowed because OBJ is not in the
forbidden extensions list — it is a generic 3D mesh, not assembly-specific data.

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
| `desiredJobId` | `string` | `demonstrator-26-02-25` | Job ID requested in the `HelloRequest` capabilities |
| `enableRuntimeAssetPipeline` | `bool` | `true` | Download and present assets on step activation |
| `useHologramShader` | `bool` | `true` | Apply the cyan holographic shader to per-step animated parts |
| `fixtureOverlayPrefab` | `GameObject` | _none_ | Static fixture model to overlay on the tracked machine. Empty = no overlay |
| `vuforiaTrackingBridge` | `VuforiaTrackingBridge` | — | Bridge component forwarding Vuforia status to bootstrap |
| `autoConfirmStepAfterAssetReady` | `bool` | `false` | Auto-confirm step after asset loads |
| `autoConfirmDelaySeconds` | `float` | `0.5` | Delay before auto-confirm |
| `heartbeatIntervalSeconds` | `float` | `5` | Seconds between heartbeat messages |
| `reconnectMinIntervalSeconds` | `float` | `2` | Minimum reconnect retry interval |
| `reconnectMaxIntervalSeconds` | `float` | `20` | Maximum reconnect retry interval |
| `reconnectBackoffMultiplier` | `float` | `1.8` | Exponential backoff factor |
| `statusPanel` | `SessionStatusPanel` | — | Optional HUD panel MonoBehaviour |
| `trackingDirectionHint` | `TrackingDirectionHint` | — | Optional direction hint arrow |

**Public Methods:**

| Method | Description |
|--------|-------------|
| `ConfirmActiveStep()` | Confirm step completion; sends `StepCompleted` to server which advances to the next step |
| `ReplayActiveStep()` | Re-present the current step assets locally |
| `PreviousStep()` | Sends `UserAction(Previous)` to the server; server responds with `StepActivated` for the previous step |
| `ShowHelp()` | Display help overlay |
| `ExportDiagnosticsBundle()` | Write diagnostics JSON to `persistentDataPath` |
| `OnTargetTrackingUpdated(pos, rot, acquired)` | Called by `VuforiaTrackingBridge` on tracking change |

### `VuforiaTrackingBridge`

Attach to a Vuforia `ObserverEventHandler` GameObject. Forwards tracking events to
`AppBootstrap.OnTargetTrackingUpdated`.

### `FixtureOverlay`

Added at runtime to the Vuforia `ObserverBehaviour` after the Model Target loads.
Owns a single instance of the static fixture model and animates its appearance/disappearance
based on tracking status. See [`visual-effects.md`](./visual-effects.md).

### `HologramApplier`

Static utility called by `ModelPresenter` after each per-step GLB is loaded. Swaps every
`Renderer`'s materials for a shared cyan hologram material with a double-tap heartbeat
pulse. Toggle via `AppBootstrap.useHologramShader`.

---

## Runtime Asset Flow

When a `StepActivated` message is received:

1. `StepAssetManifestClient` fetches the manifest via HTTP to get URLs and version keys.
2. **GLB model**: downloaded via gRPC `StreamStepAsset(ASSET_TYPE_GLB)` or HTTP, cached
   in `Application.persistentDataPath/guidance-cache/{assetVersion}/`.
3. **Vuforia target**: downloaded via gRPC `StreamStepAsset(ASSET_TYPE_VUFORIA_TARGET)` or
   HTTP, cached in `Application.persistentDataPath/guidance-target-cache/{targetVersion}/`.
4. `ModelPresenter.PresentModelAsync()` asynchronously loads the GLB using glTFast.
5. After load, `HologramApplier.Apply()` swaps materials to the hologram shader (if enabled).
6. `TargetManager.ActivateTarget()` registers the target with Vuforia
   (via `VuforiaModelTargetLoader` if Vuforia Engine is present).
7. On first tracking acquisition, `FixtureOverlay` instantiates the fixture prefab as a
   child of the Vuforia observer transform and runs the slice-plane reveal animation.

All downloads are **version-keyed** — if the file is already cached for the current
version, no network request is made.

---

## User Actions

The wire protocol (`UserActionType` enum in `proto/guidance.proto`) supports:

| Action | Client method | Server effect |
|---|---|---|
| `CONFIRM` | `ConfirmActiveStep()` (uses `StepCompleted` shortcut) | Advance to next step |
| `NEXT` | `ConfirmActiveStep()` (same as Confirm in current implementation) | Advance to next step |
| `PREVIOUS` | `PreviousStep()` | Server sends `StepActivated` for the previous step in the job sequence |
| `REPLAY` | (not yet wired in UI) | Server re-emits the current step's `StepActivated` |
| `HELP` | (not yet wired in UI) | Reserved |

The server (`server-kit/app/grpc_session_service.py`) handles `PREVIOUS` and `REPLAY` in
the `Connect` duplex loop's `user_action` payload branch.

---

## Changing the Server Address

1. Select the `AppBootstrap` GameObject in the scene hierarchy.
2. In the Inspector, update **gRPC Target** (e.g. `192.168.1.100:50051`)
   and/or **Http Bridge Base Url** (e.g. `http://192.168.1.100:8080`).
3. Save the scene and rebuild.

> **Heads up** — this requires a rebuild every time. For a runtime-configurable approach
> (config file on the device, or in-app settings screen), see the discussion in this
> branch's working notes — the implementation has not yet landed.

---

## Build Settings

- **Unity version**: 6000.0.54f1 (Unity 6 LTS line)
- **Platform**: Android (VUZIX M4000, ARM64, API level 30/Android 11)
- **Scripting Backend**: IL2CPP
- **Target Architectures**: ARM64 only (uncheck ARMv7 for ~40% smaller native libs)
- **API Compatibility Level**: .NET Standard 2.1
- **Managed Stripping Level**: High (recommended; `link.xml` preserves the gRPC stack)
- **Active Render Pipeline**: Built-in (the hologram and fixture-reveal shaders are HLSL/CG against the Built-in pipeline; URP is _not_ required)
- The `#if VUFORIA_ENGINE` guards in `VuforiaModelTargetLoader.cs` activate automatically
  when the Vuforia Engine package is imported.
- The `#if UNITY_EDITOR` guards in any test stubs ensure mock data never reaches a build.

---

## See Also

- [`visual-effects.md`](./visual-effects.md) — hologram shader, fixture reveal shader, tuning guide
- [`server-setup.md`](./server-setup.md) — running FastAPI and gRPC servers
- [`proto-reference.md`](./proto-reference.md) — wire protocol message types
- [`operator-guide.md`](./operator-guide.md) — daily operation on the VUZIX
