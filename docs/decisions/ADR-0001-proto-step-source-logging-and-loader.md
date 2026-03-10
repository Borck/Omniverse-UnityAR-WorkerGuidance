# ADR-0001: Proto Workflow, Step Source, Logging, and Loader

Date: 2026-03-10
Status: Accepted

## Context
The project required decisions for practical Unity protobuf generation, canonical step-definition source, structured logging format, and runtime model loader direction.

## Decisions
1. C# protobuf/gRPC generation workflow uses a dedicated `Grpc.Tools` build project:
   - `tools/proto-csharp/ProtoCSharpGen.csproj`
   - output to `client-unity/Assets/App/Generated`
2. Canonical step-definition source is external YAML:
   - `shared/samples/step-definitions.yaml`
3. Structured logging schema is JSON lines with fixed keys:
   - `timestamp`, `level`, `logger`, `event`, `message`, `session_id`, `step_id`, `correlation_id`
4. Runtime loader direction for Unity is `glTFast`.
5. GLB transfer is supported over gRPC via `AssetTransferService.StreamStepAsset` (chunked stream).

## Consequences
- Python and Unity protobuf workflows are decoupled and reproducible.
- Step authoring can evolve without recompiling binaries.
- Logs are machine-readable for diagnostics and telemetry processing.
- Unity runtime integration should target `glTFast` APIs.
- Draco compression metadata and negotiation are in contract, but server-side Draco encoding implementation is still pending.

## Follow-up
- Decide and implement the server-side Draco encoder path in export/packaging pipeline.
