# Omniverse Unity AR Worker Guidance

This repository contains the first implementation slice for an AR worker guidance system.

## Components
- `server-kit/`: Omniverse Kit server for step resolution, export, gRPC, and HTTP services.
- `client-unity/`: Unity 6 client runtime for session orchestration, model loading, and tracking.
- `proto/`: Shared protobuf contracts used by server and client.
- `shared/`: Shared schemas, sample payloads, and fixtures.

## Current Status
- Repository skeleton created.
- Initial protobuf contract drafted.
- Developer workspace/tasks scaffolded.
- Minimal health endpoint server skeleton added.

## Quick Start
1. Open `Omniverse-UnityAR-WorkerGuidance.code-workspace` in VS Code.
2. Review and install recommended extensions.
3. Run task `Proto: Generate Python Stubs` after installing `grpcio-tools`.
4. Run task `Server: Run Health Endpoint` to verify local startup.

## Next
- Implement `guidance.proto` code generation for C#.
- Add Kit extension bootstrapping and stage-open service.
- Add Unity session client shell and mock step display.
