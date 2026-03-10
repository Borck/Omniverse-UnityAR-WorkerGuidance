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
- `asset_version` and `target_version` should be immutable content hashes.
- `Fault.correlation_id` should be propagated in logs for troubleshooting.
- `StepAssetStreamRequest.preferred_compression` enables transport negotiation (`NONE`/`DRACO`).
- `StepAssetChunk.applied_compression` reports actual compression used by the server.
