# Asset Cache Layout

Date: 2026-03-10
Status: Defined (filesystem convention)

## Goals
- Keep runtime assets immutable and content-addressable.
- Allow safe reuse across sessions/jobs.
- Enable simple cleanup strategies by version path.

## Roots
Configured in `AppConfig`:
- Source assets: `GUIDANCE_ASSET_ROOT`
- Source targets: `GUIDANCE_TARGET_ROOT`
- Exported assets: `GUIDANCE_EXPORT_ASSET_ROOT`
- Exported manifests: `GUIDANCE_EXPORT_MANIFEST_ROOT`

Default roots:
- `shared/samples/assets`
- `shared/samples/targets`
- `shared/samples/assets`
- `shared/samples/manifests`

## Directory Structure
```text
assets/
  sha256_<asset-version>/
    <part>.glb
    <step>.json

targets/
  <target-version>/
    <target-file>

manifests/
  <job-id>.manifest.json
```

## Naming Rules
- `assetVersion` is hash-scoped and immutable once published.
- `targetVersion` is immutable for published target payloads.
- Manifest references must only point to immutable versioned paths.

## Write Rules
- Export pipeline writes new version directories; it does not mutate existing version directories.
- If output for a hash already exists, exporter may reuse existing content.
- Manifest rewrite updates each step to generated immutable versions.

## Read Rules
- HTTP and gRPC services resolve files by version + file name only.
- Callers must treat versioned URLs as immutable artifacts.

## Cleanup Guidance
- Remove only terminal job records via queue cleanup endpoint.
- Asset/target content cleanup should be a separate retention task, constrained by manifest reachability.
