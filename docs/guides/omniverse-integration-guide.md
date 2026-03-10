# Omniverse Integration Guide (M13)

Updated: 2026-03-10

## Prerequisites
- Python environment with `server-kit/app/requirements.txt` installed
- Omniverse-compatible stage URI configured (`GUIDANCE_STAGE_URI`)
- Step definitions available in `shared/samples/step-definitions.yaml` or chosen source

## Server Setup
1. Start HTTP API:
- `python -m uvicorn server_kit_main:app --host 0.0.0.0 --port 8080 --app-dir server-kit/app`
2. Start gRPC server (if native transport path is used):
- `python server-kit/app/grpc_server_main.py`

## Stage/Open Validation
1. Set environment variable `GUIDANCE_STAGE_URI`.
2. Execute `POST /api/stage:open-smoke`.
3. Resolve URI or scheme issues before runtime tests.

## Step Resolver Integration
1. Keep canonical step definitions external and version-controlled.
2. Use `POST /api/jobs/{jobId}/layers:resolve` to preview BTU-style layer projection.
3. Verify deterministic cache keys for identical inputs.

## Export Integration
1. Trigger package generation: `POST /api/jobs/{jobId}/packages:build`.
2. Monitor status: `GET /api/package-jobs/{runId}`.
3. Serve generated assets and manifests from configured output paths.

## Operational Notes
- Use `GUIDANCE_EXPORT_JOB_PROCESSING_MODE=enqueue-only` when dedicated worker owns processing.
- Keep Draco optional and fallback-capable for portability.
