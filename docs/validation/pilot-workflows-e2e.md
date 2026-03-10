# Pilot Workflows End-To-End Validation (M12)

Updated: 2026-03-10

This checklist captures 3-5 representative workflows that must pass before pilot rollout.

## Status
- Automated server-side validation: Passed (43/43 tests via `tools/scripts/run-validation-matrix.ps1`).
- Device workflow evidence folders and run sheets: Prepared.
- Final M12 closure: Pending manual target-device execution for `PW-01`..`PW-05`.

## Workflow Set

| Workflow ID | Description | Steps | Required Transport | Result |
|---|---|---:|---|---|
| PW-01 | Baseline assembly sequence | 5 | Native gRPC | Pending |
| PW-02 | HTTP bridge fallback sequence | 5 | HTTP bridge | Pending |
| PW-03 | Mid-step network drop and recovery | 5 | Native gRPC | Pending |
| PW-04 | Restart/resume with cached assets | 5 | Native gRPC | Pending |
| PW-05 | Operator-assisted help/replay path | 5 | HTTP bridge | Pending |

## Validation Sheet Per Workflow
- Build ID:
- Device ID:
- Date/time:
- Operator:
- Server commit:
- Unity commit:
- Preconditions met (`yes/no`):

## Procedure
1. Start from clean app launch.
2. Connect to the configured server target.
3. Execute each step with expected user interactions (`confirm`, `replay`, `help` where applicable).
4. Record warnings, recoveries, and telemetry anomalies.
5. Export diagnostics bundle after completion.

## Success Criteria
- Guidance loop completes without deadlock or crash.
- Active model/target pair remains synchronized per step.
- Confirm action advances sequence deterministically.
- Reconnect behavior preserves step context during temporary outages.

## Evidence To Attach
- Diagnostics JSON from `DiagnosticsBundleExporter`.
- Screen recording for each workflow.
- Server log extract with `session_id` and `step_id` transitions.

## Completion Gate
- [ ] PW-01 pass recorded in `shared/fixtures/pilot-evidence/PW-01/run-sheet.md`.
- [ ] PW-02 pass recorded in `shared/fixtures/pilot-evidence/PW-02/run-sheet.md`.
- [ ] PW-03 pass recorded in `shared/fixtures/pilot-evidence/PW-03/run-sheet.md`.
- [ ] PW-04 pass recorded in `shared/fixtures/pilot-evidence/PW-04/run-sheet.md`.
- [ ] PW-05 pass recorded in `shared/fixtures/pilot-evidence/PW-05/run-sheet.md`.
