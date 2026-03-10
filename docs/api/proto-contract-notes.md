# Proto Contract Notes

This file tracks compatibility and implementation notes for `proto/guidance.proto`.

## Versioning
- Proto package: `guidance.v1`
- Breaking changes require a new package version.
- New optional fields should be added with new field numbers only.

## Services
- `GuidanceSessionService.Connect`: bidirectional control stream.
- `AssetQueryService.GetManifest`: immutable asset manifest query.
- `AssetTransferService.StreamStepAsset`: chunked GLB delivery over gRPC.

## Implementation Notes
- Client and server must treat duplicate `StepActivated`/`StepCompleted` as idempotent.
- Server session stream currently ignores duplicate `hello` and duplicate `step_completed` messages within the same stream.
- Session continuity uses `HelloRequest.device_id` as resume key and persists to `GUIDANCE_SESSION_STORE_FILE`.
- `asset_version` and `target_version` should be immutable content hashes.
- `Fault.correlation_id` should be propagated in logs for troubleshooting.
- `StepAssetStreamRequest.preferred_compression` enables transport negotiation (`NONE`/`DRACO`).
- `StepAssetChunk.applied_compression` reports actual compression used by the server.

## HTTP Runtime Notes
- `POST /api/jobs/{jobId}/packages:build` returns `processingMode` to report queue-processing ownership.
- Processing ownership is configured by `GUIDANCE_EXPORT_JOB_PROCESSING_MODE`:
	- `inline`: API enqueues and schedules in-process execution.
	- `enqueue-only`: API only enqueues; dedicated export worker processes queued runs.
