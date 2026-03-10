# ADR-0005: Unity Native gRPC Without Proxy As Default

Date: 2026-03-10
Status: Accepted
Supersedes: ADR-0004

## Context
The transport must remain pure gRPC or gRPC-Web while integrating natively into Unity and Omniverse without requiring a proxy container in the default setup.

The repository already has a running native gRPC server (`server-kit/app/grpc_server_main.py`) and generated Unity C# gRPC client stubs (`client-unity/Assets/App/Generated/GuidanceGrpc.cs`).

## Decision
Use native direct gRPC as the default Unity transport path:
- Unity session client connects directly to gRPC target `<host>:50051`.
- No proxy container is required for the default integration path.
- HTTP bridge remains available only as runtime fallback for environments where native gRPC transport cannot be used.
- Envoy gRPC-Web remains optional and non-default.

## Consequences
- Protocol path is pure gRPC by default.
- Operational setup is simpler for normal development and Omniverse integration.
- Runtime transport stays abstracted (`ISessionTransport`), so fallback/profiles can be switched without touching guidance logic.

## Implementation Notes
- New Unity transport: `client-unity/Assets/App/Networking/GrpcSessionTransport.cs`
- Default runtime toggle in `AppBootstrap`:
  - `useNativeGrpcTransport = true`
  - `grpcTarget = "localhost:50051"`
- Fallback path:
  - `useNativeGrpcTransport = false`
  - `HttpBridgeSessionTransport` over `http://localhost:8080`
