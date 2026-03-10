# PW-04 Run Sheet

## Run Metadata
- Workflow ID (`PW-01`..`PW-05`): PW-04
- Run ID: PW-04-RUN-001
- Date/time (UTC):
- Operator:
- Device model + OS:
- Unity build ID:
- Server commit:
- Unity commit:
- Transport (`gRPC` or `HTTP bridge`): gRPC

## Preconditions
- [ ] Server health endpoint reachable.
- [ ] Manifest endpoint for selected job reachable.
- [ ] Required assets and target payloads available.
- [ ] Diagnostics export action available in HUD.

## Step Execution Log
| Step | Action Performed | Expected Outcome | Actual Outcome | Pass/Fail | Notes |
|---|---|---|---|---|---|
| 1 | execute steps 1-2 | assets cached |  |  |  |
| 2 | app restart | reconnect succeeds |  |  |  |
| 3 | resume step flow | cached assets reused |  |  |  |
| 4 | confirm | deterministic next-step activation |  |  |  |
| 5 | complete workflow | no crash/deadlock |  |  |  |

## Performance Snapshot
- Median frame time (ms):
- p95 frame time (ms):
- Memory delta over run (MB):
- Thermal warnings/throttling observed:

## Faults And Recovery
- Fault codes/messages observed:
- Recovery actions applied:
- Was completion possible without restart? (`yes/no`):

## Evidence Collected
- Diagnostics JSON path: `shared/fixtures/pilot-evidence/PW-04/diagnostics/`
- Screen recording path: `shared/fixtures/pilot-evidence/PW-04/recordings/`
- Server log extract path: `shared/fixtures/pilot-evidence/PW-04/server-logs/`
- Extra artifacts:

## Sign-off
- Overall result (`pass/conditional/fail`):
- Reviewer:
- Follow-up issues:
