# Kit Extension Dependency Graph

Date: 2026-03-10
Status: Initial draft (scaffold-aligned)

## Context
The repository includes extension placeholders under `server-kit/exts/`:
- `company.guidance.core`
- `company.guidance.export`
- `company.guidance.grpc`
- `company.guidance.http`
- `company.guidance.tests`

At this stage these folders are scaffolds (no extension manifests yet). This document captures the intended dependency direction to keep implementation consistent once extension manifests and code are added.

## Intended Dependency Direction
- `company.guidance.core`
  - Base domain contracts, shared models, logging helpers.
  - Depends only on Omni/Kit SDK foundations.
- `company.guidance.export`
  - Export orchestration, packaging, hash/version materialization.
  - Depends on `company.guidance.core`.
- `company.guidance.grpc`
  - gRPC session and asset streaming adapters.
  - Depends on `company.guidance.core` and `company.guidance.export`.
- `company.guidance.http`
  - HTTP surface for health, manifests/assets, package-job lifecycle.
  - Depends on `company.guidance.core` and `company.guidance.export`.
- `company.guidance.tests`
  - Integration and behavioral tests.
  - Depends on all runtime extensions as test targets.

## Graph (Logical)
```text
company.guidance.core
  -> company.guidance.export
    -> company.guidance.grpc
    -> company.guidance.http
      -> company.guidance.tests
```

## Rules
- No runtime extension may depend on `company.guidance.tests`.
- Keep transport concerns (`grpc`, `http`) out of `core`.
- Keep business/export logic in `export` so it can be reused by both transports.
- Prefer one-way dependencies to avoid circular initialization in Kit.

## Follow-up
- Add concrete extension manifests (for example `extension.toml`) for each package.
- Validate this graph against real startup order once Kit extension loading is wired.
