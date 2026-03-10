# ADR-0004: Unity Direct HTTP Bridge As Default Transport

Date: 2026-03-10
Status: Superseded by ADR-0005
Supersedes: ADR-0003

## Context
The project needs a mobile-safe Unity transport for Android 10+ and Unity 6 with minimal operational complexity for local development and Omniverse integration.

A proxy container (Envoy) adds extra runtime setup, network routing, and failure modes. The server now exposes HTTP session bridge endpoints (`/session/connect`, `/session/heartbeat`) that satisfy the current vertical slice requirements directly.

## Decision
Use direct HTTP bridge transport as the default runtime path:
- Unity session transport targets the HTTP server directly (`http://<host-or-lan-ip>:8080`).
- Unity uses `POST /session/connect` and `POST /session/heartbeat` for handshake and keepalive.
- Envoy is optional and only used for explicit gRPC-Web experiments.

Update: superseded by ADR-0005 where native direct gRPC is the default and HTTP bridge is fallback.

## Consequences
- Simpler setup: no proxy container required.
- Fewer moving parts in Unity and Omniverse development loops.
- Existing transport abstraction (`ISessionTransport`) remains, so future protocol upgrades are still possible.
- Full bidirectional gRPC semantics remain deferred until explicitly needed.

## Implementation Notes
- Unity defaults:
  - `SessionClient`: `http://localhost:8080`
  - `AppBootstrap.sessionBaseUrl`: `http://localhost:8080`
- Server endpoints:
  - `POST /session/connect`
  - `POST /session/heartbeat`
- Reference API docs: `docs/api/http-runtime-api.md`
