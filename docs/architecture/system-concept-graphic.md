# System Concept Graphic (M13)

Updated: 2026-03-10

```mermaid
flowchart LR
    U[Unity 6 Client]\nAppBootstrap + StepCoordinator
    T[Transport Layer]\nNative gRPC / HTTP Bridge
    S[Server Kit API]\nFastAPI + Session Manager
    R[Step Resolver]\nYAML step definitions + layer resolver
    E[Export Pipeline]\nGLB export + optional Draco
    A[(Versioned Asset Store)]
    V[Vuforia Runtime]\nTarget tracking callbacks
    D[(Diagnostics Bundle)]

    U --> T
    T --> S
    S --> R
    R --> E
    E --> A
    A --> U
    V --> U
    U --> D
    S --> D
```

## Communication Path
1. Unity connects via native gRPC (default) or HTTP bridge fallback.
2. Server resolves next step context from step definitions and layer-state projection.
3. Export pipeline creates deterministic, versioned asset payloads.
4. Unity resolves manifest URLs, downloads/caches active and next assets, and presents one active model.
5. Tracking feedback updates the runtime state and user guidance loop.
6. Diagnostics are exportable from the client and correlatable with server logs.
