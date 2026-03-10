# ADR-0003: Unity Android Transport via Envoy gRPC-Web Gateway

Date: 2026-03-10
Status: Accepted

## Context
Unity 6 on Android 10+ should avoid direct coupling to `Grpc.Core` runtime behavior when possible. The project already introduced a transport abstraction in the Unity client.

## Decision
Use Envoy as the default development and deployment bridge for Unity session transport:
- Unity client targets HTTP endpoint at Envoy listener (`:8081`).
- Envoy `grpc_web` filter bridges requests to backend gRPC server (`:50051`).
- Unity transport remains abstracted (`ISessionTransport`) so gateway implementation can evolve without rewriting app logic.

## Consequences
- Better mobile/runtime compatibility path for Android.
- Clear separation between gameplay/session logic and wire transport.
- Adds operational dependency on gateway process/container.

## Implementation Notes
- Envoy config: `tools/dev/envoy/envoy.yaml`
- Local run task: `.vscode/tasks.json` (`Gateway: Run Envoy gRPC-Web (Docker)`)
- Unity default bridge URL for session transport: `http://localhost:8081` (use LAN IP on physical device).
