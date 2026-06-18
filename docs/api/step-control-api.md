# External Step Control API
<!-- Sa/changes -->
How an external system drives which step a connected device (e.g. Vuzix) is on —
**without** being the device's session client.

> **Protocol requirement:** the controller must speak **gRPC** (it calls
> `ControlStep` as a gRPC client). "External system" here describes the *role* —
> a supervisor dashboard, MES, test harness, another backend service, etc. — not
> the protocol. Whatever that system is, with the current implementation it
> connects over gRPC. If it cannot speak gRPC natively, see
> [Non-gRPC callers](#non-grpc-callers) below.

Defined in [`proto/guidance.proto`](../../proto/guidance.proto) as
`GuidanceControlService`. Implemented in
[`server-kit/app/grpc_control_service.py`](../../server-kit/app/grpc_control_service.py).

---

## Mental model (read this first)

There is **one** guidance gRPC server. The device and the controller are **two
clients of that same server** — not two servers.

```
            ┌───────────────────────────────────────────┐
 device ───▶│  guidance gRPC server  @  HOST:50051        │◀─── your control client
 (Connect   │  GuidanceSessionService  (device stream)    │     (ControlStep)
  stream)   │  GuidanceControlService  (your control RPC) │
            │  share one in-memory SessionChannels        │
            └───────────────────────────────────────────┘
```

The control push travels through an **in-memory** `SessionChannels` object inside
the server process. Therefore:

> **The device's `Connect()` stream and your `ControlStep` call must reach the
> same running server process.**

Both services are registered on the **same server and same port** (`50051`), so
this is automatic — *as long as there is only one server*. Running a second copy
of the server in another process will **not** work: it has its own empty
`SessionChannels` and the device is not connected to it.

You do **not** run your own server. You connect to the existing one as a client.

---

## What your agent needs to connect

| Thing | Value |
|---|---|
| Transport | gRPC over HTTP/2 (plaintext / insecure on LAN) |
| Address | `HOST:50051` — the **same** host:port the device connects to |
| Service | `guidance.v1.GuidanceControlService` |
| Method | `ControlStep` (unary: one request → one response) |
| Contract | [`proto/guidance.proto`](../../proto/guidance.proto) |

Port/host are configurable on the server via `GUIDANCE_GRPC_HOST` (default
`0.0.0.0`) and `GUIDANCE_GRPC_PORT` (default `50051`).

### Generating stubs

Your agent needs the proto to generate client stubs in its language:

```bash
# Python (reuse the same command the repo uses)
python -m grpc_tools.protoc -I proto \
  --python_out=OUT --grpc_python_out=OUT proto/guidance.proto
```

For other languages, point your language's protoc plugin at
`proto/guidance.proto`. gRPC is cross-language — the controller does **not** have
to be Python.

---

## The RPC

```proto
service GuidanceControlService {
  rpc ControlStep(ControlStepRequest) returns (ControlStepResponse);
}

message ControlStepRequest {
  string job_id = 1;          // MUST match the device session's active job
  ControlAction action = 2;   // GOTO is the only implemented action (see Limitations)
  string step_id = 3;         // required for GOTO, e.g. "step-003"
}

message ControlStepResponse {
  bool   ok = 1;                 // request was valid and dispatched
  string activated_step_id = 2;  // the step that was pushed
  int32  sessions_notified = 3;  // how many device sessions received it
  string message = 4;            // human-readable detail (esp. on failure)
}

enum ControlAction {
  CONTROL_ACTION_UNSPECIFIED = 0;
  CONTROL_ACTION_NEXT        = 1;  // not yet implemented
  CONTROL_ACTION_PREVIOUS    = 2;  // not yet implemented
  CONTROL_ACTION_GOTO        = 3;  // jump to step_id
}
```

### Behaviour

- The server resolves `step_id` within `job_id` and pushes a `StepActivated` to
  every device session currently on that job.
- The device renders the step exactly as if it had advanced normally — no device
  change is required (it handles the pushed `StepActivated` already).

---

## Minimal client example (Python)

```python
import grpc
from generated import guidance_pb2, guidance_pb2_grpc

with grpc.insecure_channel("HOST:50051") as channel:
    stub = guidance_pb2_grpc.GuidanceControlServiceStub(channel)
    resp = stub.ControlStep(guidance_pb2.ControlStepRequest(
        job_id="pu-segment-assembly-instrutions",   # must match the device's job
        action=guidance_pb2.CONTROL_ACTION_GOTO,
        step_id="step-003",
    ))
    print(resp.ok, resp.activated_step_id, resp.sessions_notified, resp.message)
```

Interpreting the response:

- `ok == True, sessions_notified >= 1` → delivered to that many devices.
- `ok == True, sessions_notified == 0` → step is valid but **no device is on
  that job** (usually a `job_id` mismatch — see gotchas).
- `ok == False` → bad request; read `message` (unknown step, missing fields, or
  an unsupported action).

---

## Gotchas

1. **`job_id` must match the device's active job.** `ControlStep` only reaches
   sessions whose active job equals the `job_id` you send. The device's job is
   set by its `hello` capabilities (`job=<id>`) or the server default
   (`job-mock-001`). If they differ you get `sessions_notified == 0` and the
   device does not move. Always check `sessions_notified`.

2. **Delivery latency ≈ one heartbeat.** Pushes are flushed to the device the
   next time it sends *any* message; the device heartbeats every ~5s
   (`heartbeatIntervalSeconds` on the Unity `AppBootstrap`). So a GOTO can take
   up to ~5s to appear. Lower the heartbeat interval for snappier control.

3. **All sessions on the job are targeted.** `ControlStep` broadcasts to every
   device on `job_id`. If you must address one specific device, a
   per-session targeting variant would need to be added.

4. **No auth on the LAN channel.** The server uses an insecure channel; anyone
   who can reach `:50051` can drive the device. Add authentication before
   production.

---

## Non-gRPC callers

If the controlling system cannot make a native gRPC call, pick one:

| Caller | Can it call `ControlStep` directly? | What it needs |
| --- | --- | --- |
| Backend service / script (any language) | ✅ Yes | Generate stubs from `proto/guidance.proto` and call over gRPC |
| Browser-based dashboard | ❌ Not raw gRPC | **gRPC-web + Envoy proxy** in front of the server (see the "Gateway… Envoy gRPC-Web" task in `.vscode/tasks.json`) |
| PLC / HTTP-only / legacy system | ❌ Not gRPC | Either the gRPC-web proxy, or an **HTTP control endpoint** (not yet built — would have to be hosted *in the gRPC process* so it shares `SessionChannels`) |

The key constraint is unchanged regardless of route: whatever finally calls into
the server must reach the **same process** that holds the device's `Connect`
stream.

## Limitations (current MVP)

- **GOTO only.** `NEXT` and `PREVIOUS` return `ok == False` with an explanatory
  `message`. They require the server to track each session's *current* step,
  which is not yet implemented.
- **Single process requirement.** As described in the mental model, control only
  works when the controller hits the same server process that holds the device
  stream.

---

## Quick checklist for the integrator

- [ ] One guidance gRPC server is running and reachable at `HOST:50051`.
- [ ] The device (Unity `grpcTarget`) points at that same `HOST:50051`.
- [ ] Your controller has stubs generated from `proto/guidance.proto`.
- [ ] Your controller connects to the **same** `HOST:50051` (not a second server).
- [ ] Your `ControlStepRequest.job_id` matches the device's active job.
- [ ] You check `ControlStepResponse.sessions_notified > 0` to confirm delivery.
