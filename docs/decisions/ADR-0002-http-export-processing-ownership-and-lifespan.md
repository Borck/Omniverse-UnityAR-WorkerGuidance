# ADR-0002: Export Processing Ownership and FastAPI Lifespan

Date: 2026-03-10
Status: Accepted

## Context
The package-build HTTP endpoint and dedicated export worker both had access to processing queued export jobs. Without an ownership rule, production deployments risk duplicate processors. The server also emitted FastAPI startup deprecation warnings due to `on_event("startup")` usage.

## Decisions
1. Add explicit export processing ownership mode via environment configuration:
   - `GUIDANCE_EXPORT_JOB_PROCESSING_MODE=inline|enqueue-only`
2. Define runtime behavior by mode:
   - `inline` (default): HTTP API enqueues and schedules in-process background processing.
   - `enqueue-only`: HTTP API only enqueues jobs; dedicated worker process is the only processor.
3. Include `processingMode` in `POST /api/jobs/{jobId}/packages:build` response payload for operational visibility.
4. Migrate FastAPI startup hook to lifespan handler to replace deprecated `on_event("startup")` usage.

## Consequences
- Deployments can explicitly choose a single processing authority.
- Local development remains simple with default inline processing.
- Production-like deployments can isolate processing in worker processes.
- Startup lifecycle is aligned with current FastAPI guidance and no longer emits startup-hook deprecation warnings.

## Follow-up
- If multi-instance processing is required, replace JSON file queue storage with a concurrency-safe backend (for example SQLite or Redis).
- Unity client transport direction for Android 10+ and Unity 6 is a transport abstraction with HTTP bridge/gRPC-Web-ready implementation as default, instead of direct `Grpc.Core` runtime coupling.
