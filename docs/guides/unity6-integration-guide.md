# Unity 6 Integration Guide (M13)

Updated: 2026-03-10

## Prerequisites
- Unity 6 project opened from `client-unity/`
- Generated C# protobuf files in `Assets/App/Generated/`
- Server endpoints reachable from device or editor

## Package Setup
1. Verify `glTFast` and Unity test framework entries in `client-unity/Packages/manifest.json`.
2. Ensure generated gRPC/proto C# files compile in the Unity project.

## Runtime Setup
1. Add `AppBootstrap` to a startup GameObject.
2. Assign optional UI references:
- `SessionStatusPanel`
- `TrackingDirectionHint`
3. Configure transport values in inspector:
- `useNativeGrpcTransport=true` and `grpcTarget=<host>:50051` for default.
- or set `useNativeGrpcTransport=false` with `httpBridgeBaseUrl=http://<host>:8080`.

## Step Runtime Flow
1. App initializes runtime context and session client.
2. Session connection emits first `StepActivated`.
3. Manifest resolves current and next step assets.
4. Assets and target payloads are downloaded/cached.
5. Target manager and model presenter activate current step visuals.
6. User confirms step to advance progression.

## Validation
- Run EditMode tests in `Assets/App/Tests/Editor`.
- Verify HUD actions (`replay`, `next`, `previous`, `confirm`, `help`).
- Test reconnect and frozen-step behavior.
