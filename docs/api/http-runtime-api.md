# HTTP Runtime API

Date: 2026-03-10
Status: Implemented endpoints

## Base
- Local default base URL: `http://localhost:8080`

## Endpoints

### `GET /health`
Returns service health.

Response `200`:
```json
{"status":"ok"}
```

### `GET /api/jobs/{jobId}/manifest`
Returns job manifest with asset and target URLs.

Response `200` fields:
- `jobId`
- `workflowVersion`
- `steps[]`
  - `stepId`, `partId`, `assetVersion`, `glbUrl`, `stepJsonUrl`, `targetVersion`, `targetUrl`, `compression`

Headers:
- `Cache-Control: public, max-age=60`
- `ETag: "manifest-{jobId}"`

### `POST /api/jobs/{jobId}/packages:build`
Enqueues package build for a job.

Response `202` fields:
- `jobId`
- `runId`
- `state`
- `processingMode` (`inline` or `enqueue-only`)
- `statusUrl`

Behavior:
- `GUIDANCE_EXPORT_JOB_PROCESSING_MODE=inline`: enqueue + in-process background execution.
- `GUIDANCE_EXPORT_JOB_PROCESSING_MODE=enqueue-only`: enqueue only (worker process should process queue).

### `GET /api/package-jobs/{runId}`
Returns package-job status.

Response `200` fields:
- `runId`, `jobId`, `state`, `createdAt`, `updatedAt`, `generatedSteps`, `manifestPath`, `error`

States:
- `queued`, `running`, `succeeded`, `failed`, `canceled`

### `DELETE /api/package-jobs/{runId}`
Attempts to cancel package job.

Response `200`:
- `runId`, `jobId`, `state` (`canceled`)

Possible errors:
- `404` if run does not exist
- `409` if job is not cancelable in current state

### `POST /api/package-jobs:cleanup`
Cleans terminal jobs older than retention TTL.

Query parameter:
- `ttl_seconds` (optional)

Response `200`:
- `removed`
- `ttlSeconds`

### `GET /api/assets/{assetVersion}/{fileName}`
Returns versioned asset file.

Headers:
- `Cache-Control: public, immutable, max-age=31536000`
- `ETag: "asset-{assetVersion}-{fileName}"`

### `GET /api/targets/{targetVersion}/{fileName}`
Returns versioned target file.

Headers:
- `Cache-Control: public, immutable, max-age=31536000`
- `ETag: "target-{targetVersion}-{fileName}"`

## Operational Notes
- Dedicated export worker command: `python server-kit/app/export_worker_main.py`
- Worker poll interval: `GUIDANCE_EXPORT_WORKER_POLL_SECONDS`
- Queue store path: `GUIDANCE_EXPORT_JOB_STORE_FILE`
