# Pilot Run Sheet Template (M12)

Updated: 2026-03-10

Use one copy of this sheet per executed workflow run.

## Run Metadata
- Workflow ID (`PW-01`..`PW-05`):
- Run ID:
- Date/time (UTC):
- Operator:
- Device model + OS:
- Unity build ID:
- Server commit:
- Unity commit:
- Transport (`gRPC` or `HTTP bridge`):

## Preconditions
- [ ] Server health endpoint reachable.
- [ ] Manifest endpoint for selected job reachable.
- [ ] Required assets and target payloads available.
- [ ] Diagnostics export action available in HUD.

## Step Execution Log
| Step | Action Performed | Expected Outcome | Actual Outcome | Pass/Fail | Notes |
|---|---|---|---|---|---|
| 1 | connect + activate | step enters ready/tracking |  |  |  |
| 2 | confirm | next step activated |  |  |  |
| 3 | replay/help (if applicable) | runtime stays stable |  |  |  |
| 4 | network interruption test (if applicable) | frozen-step then recover |  |  |  |
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
- Diagnostics JSON path:
- Screen recording path:
- Server log extract path:
- Extra artifacts:

## Sign-off
- Overall result (`pass/conditional/fail`):
- Reviewer:
- Follow-up issues:
