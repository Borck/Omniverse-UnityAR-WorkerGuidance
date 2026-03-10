# Asset Download Behavior

Date: 2026-03-10
Status: Defined

## Scope
Applies to:
- `GET /api/assets/{assetVersion}/{fileName}`
- `GET /api/targets/{targetVersion}/{fileName}`

## Cache and Identity
- Responses include immutable caching headers:
  - `Cache-Control: public, immutable, max-age=31536000`
- Responses include stable ETag values:
  - Assets: `"asset-{assetVersion}-{fileName}"`
  - Targets: `"target-{targetVersion}-{fileName}"`

## Retry-Safe Behavior
- Endpoints are read-only and idempotent (`GET`).
- Client retries are safe for transient transport failures.
- Recommended client strategy:
  - Exponential backoff with jitter.
  - Max retry count per download.
  - Respect cancellation if step changes.

## Range Requests
- Runtime uses FastAPI `FileResponse`, which supports HTTP range requests where client/server stack allows.
- Clients may request byte ranges for resumable downloads.
- If range is unsupported by an intermediary path, client should retry full-file download.

## Error Semantics
- `404` when requested version/file path is missing.
- `200` for successful full response.
- `206` may be returned for successful partial content when range requests are honored.

## Client Recommendations
- Validate downloaded bytes against expected artifact semantics before use.
- Prefer immutable URL cache reuse per `assetVersion`/`targetVersion`.
- Do not overwrite a cached version with content from a different version key.
