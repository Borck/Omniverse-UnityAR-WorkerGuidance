# Proto Reference — `guidance.proto`

Source: [`proto/guidance.proto`](../proto/guidance.proto)  
C# namespace: `Guidance.V1`  
Python package: `guidance.v1`

---

## Services

### `GuidanceSessionService`

Bidirectional streaming RPC between the AR device and the guidance server.

```protobuf
service GuidanceSessionService {
  rpc Connect(stream ClientMessage) returns (stream ServerMessage);
}
```

The client opens a long-lived stream and exchanges `ClientMessage` / `ServerMessage` frames
for the duration of a session. Heartbeats keep the connection alive; the server pushes
`StepActivated` when the operator triggers a step.

---

### `AssetQueryService`

Unary RPC for fetching the asset manifest for a job.

```protobuf
service AssetQueryService {
  rpc GetManifest(ManifestRequest) returns (AssetManifest);
}
```

Clients may use this instead of the HTTP manifest endpoint when operating in a pure-gRPC environment.

---

### `AssetTransferService`

Server-streaming RPC for downloading step assets in chunks.

```protobuf
service AssetTransferService {
  rpc StreamStepAsset(StepAssetStreamRequest) returns (stream StepAssetChunk);
}
```

Set `asset_type = ASSET_TYPE_GLB` (or omit) for the GLB model.  
Set `asset_type = ASSET_TYPE_VUFORIA_TARGET` for the Vuforia model target file.

---

## Client → Server Messages (`ClientMessage`)

`ClientMessage` is a `oneof` envelope — only one field is set per frame.

| Field | Type | When sent | Description |
|-------|------|-----------|-------------|
| `hello` | `HelloRequest` | On connect | Device identification and capability announcement |
| `heartbeat` | `Heartbeat` | Every N seconds | Keep-alive; triggers `Ping` response |
| `tracking_state` | `TrackingState` | On Vuforia state change | Reports whether tracking is acquired |
| `user_action` | `UserAction` | On button press | User intent (confirm, replay, next, previous, help) |
| `step_completed` | `StepCompleted` | When worker confirms step | Tells server to advance to next step |
| `fault` | `Fault` | On error | Client-side error report |

### `HelloRequest`

| Field | Type | Description |
|-------|------|-------------|
| `device_id` | `string` | Unique device identifier (`SystemInfo.deviceUniqueIdentifier`) |
| `app_version` | `string` | Unity app version string |
| `capabilities` | `string` | Comma-separated capability flags (e.g. `"unity-ar,draco"`) |

### `Heartbeat`

| Field | Type | Description |
|-------|------|-------------|
| `session_id` | `string` | Session ID received in `HelloResponse` |
| `client_time_unix_ms` | `int64` | Client wall-clock time in milliseconds since Unix epoch |

### `StepCompleted`

| Field | Type | Description |
|-------|------|-------------|
| `job_id` | `string` | Active job identifier |
| `step_id` | `string` | Completed step identifier |
| `completed_at_unix_ms` | `int64` | Completion timestamp (Unix ms) |

### `TrackingState`

| Field | Type | Description |
|-------|------|-------------|
| `job_id` | `string` | Active job identifier |
| `step_id` | `string` | Active step identifier |
| `status` | `TrackingStatus` | Current tracking status enum value |
| `confidence` | `float` | Tracking confidence in [0, 1] |

### `UserAction`

| Field | Type | Description |
|-------|------|-------------|
| `job_id` | `string` | Active job identifier |
| `step_id` | `string` | Active step identifier |
| `action` | `UserActionType` | User intent enum value |

---

## Server → Client Messages (`ServerMessage`)

`ServerMessage` is a `oneof` envelope — only one field is set per frame.

| Field | Type | When sent | Description |
|-------|------|-----------|-------------|
| `hello_response` | `HelloResponse` | After `HelloRequest` | Assigns session ID and server time |
| `assign_job` | `AssignJob` | (reserved) | Future: explicit job assignment |
| `step_activated` | `StepActivated` | When a step begins | Full step metadata including asset references |
| `cancel_step` | `CancelStep` | On server-side abort | Tells client to stop the current step |
| `ping` | `Ping` | In response to heartbeat | Round-trip latency measurement |
| `fault` | `Fault` | On server error | Recoverable/non-recoverable error with code |

### `HelloResponse`

| Field | Type | Description |
|-------|------|-------------|
| `session_id` | `string` | Server-assigned opaque session identifier |
| `protocol_version` | `string` | Server protocol version string (e.g. `"v1"`) |
| `server_time_unix_ms` | `int64` | Server wall-clock time at response creation |

### `StepActivated`

| Field | Type | Description |
|-------|------|-------------|
| `job_id` | `string` | Active job identifier |
| `step_id` | `string` | Step identifier |
| `part_id` | `string` | Part identifier (maps to a GLB model) |
| `display_name` | `string` | Human-readable step name for the HUD |
| `instructions_short` | `string` | Short instruction text for the HUD |
| `safety_notes` | `repeated string` | Safety warnings shown before the step |
| `asset_version` | `string` | Immutable version key for the GLB asset |
| `target_id` | `string` | Vuforia model target identifier |
| `target_version` | `string` | Immutable version key for the Vuforia target |
| `anchor_type` | `string` | Tracking anchor type (e.g. `"ModelTarget"`) |
| `animation_name` | `string` | Animation clip to play from the GLB |
| `prefetch_next_step_id` | `string` | Hint to prefetch assets for the next step |

### `Fault`

| Field | Type | Description |
|-------|------|-------------|
| `code` | `string` | Machine-readable error code |
| `message` | `string` | Human-readable error description |
| `correlation_id` | `string` | Trace / correlation identifier |
| `recoverable` | `bool` | Whether the client may retry automatically |

---

## Asset Transfer Messages

### `StepAssetStreamRequest`

| Field | Type | Description |
|-------|------|-------------|
| `job_id` | `string` | Job identifier |
| `step_id` | `string` | Step identifier |
| `preferred_compression` | `AssetCompression` | Requested compression (see enum) |
| `asset_type` | `AssetType` | Which asset to stream (GLB or Vuforia target) |

### `StepAssetChunk`

One message per chunk of the streamed file.

| Field | Type | Description |
|-------|------|-------------|
| `job_id` | `string` | Job identifier (echo) |
| `step_id` | `string` | Step identifier (echo) |
| `asset_version` | `string` | Immutable asset version |
| `file_name` | `string` | Original file name |
| `applied_compression` | `AssetCompression` | Actual compression applied |
| `chunk_index` | `int32` | Zero-based sequential chunk index |
| `data` | `bytes` | Raw chunk bytes |
| `is_last` | `bool` | `true` on the final chunk of the file |

### `ManifestRequest` / `AssetManifest` / `StepAssetRef`

| Message | Key Fields | Description |
|---------|-----------|-------------|
| `ManifestRequest` | `job_id`, `workflow_version` | Request for a job manifest |
| `AssetManifest` | `job_id`, `workflow_version`, `steps[]` | Complete manifest for a job |
| `StepAssetRef` | `step_id`, `part_id`, `asset_version`, `glb_url`, `target_version`, `target_url` | Per-step asset references |

---

## Enumerations

### `AssetType`

| Value | Number | Description |
|-------|--------|-------------|
| `ASSET_TYPE_UNSPECIFIED` | 0 | Default — server treats as GLB |
| `ASSET_TYPE_GLB` | 1 | Request the GLB model file |
| `ASSET_TYPE_VUFORIA_TARGET` | 2 | Request the Vuforia Model Target file |

### `AssetCompression`

| Value | Number | Description |
|-------|--------|-------------|
| `ASSET_COMPRESSION_UNSPECIFIED` | 0 | Default — no compression |
| `ASSET_COMPRESSION_NONE` | 1 | Raw bytes |
| `ASSET_COMPRESSION_DRACO` | 2 | Draco mesh compression (GLB only) |

### `TrackingStatus`

| Value | Number | Description |
|-------|--------|-------------|
| `TRACKING_STATUS_UNSPECIFIED` | 0 | Unknown |
| `TRACKING_STATUS_LOST` | 1 | Tracking signal completely lost |
| `TRACKING_STATUS_SEARCHING` | 2 | Actively searching for target |
| `TRACKING_STATUS_ACQUIRED` | 3 | Target found and tracking stable |

### `UserActionType`

| Value | Number | Description |
|-------|--------|-------------|
| `USER_ACTION_TYPE_UNSPECIFIED` | 0 | Unknown |
| `USER_ACTION_TYPE_CONFIRM` | 1 | Worker confirms the current step is complete |
| `USER_ACTION_TYPE_REPLAY` | 2 | Replay current step animation |
| `USER_ACTION_TYPE_NEXT` | 3 | Skip to next step (supervisor override) |
| `USER_ACTION_TYPE_PREVIOUS` | 4 | Go back to previous step (supervisor override) |
| `USER_ACTION_TYPE_HELP` | 5 | Request help or show guidance overlay |

---

## Versioning Strategy

- **Proto fields** use stable numeric tags. New optional fields are added with the next
  available tag number. Fields are never removed (use deprecation comments instead).
- **Service versions** are expressed in the package name (`guidance.v1`). Breaking changes
  require a new package (`guidance.v2`) with a separate service registration.
- **Asset versions** (`asset_version`, `target_version`) are opaque strings treated as
  immutable keys by both client and server. Clients cache by version; if a version string
  is unchanged, the server guarantees the file content is identical.
