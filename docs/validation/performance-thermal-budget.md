# Performance And Thermal Budget (M12)

Updated: 2026-03-10

## Status
- Automated backend prechecks: Passed (see `docs/validation/m12-validation-report.md`).
- Target-device thermal/performance run: Pending (requires physical Android pilot device).

## Objective
Validate that guidance sessions stay responsive and thermally stable on pilot target devices.

## Device Baseline Template
- Device model:
- SoC / GPU:
- OS version:
- Unity version:
- Build type (`Development` or `Release`):

## Runtime Budget Targets
- Median frame time: <= 16.7 ms (60 FPS target) or <= 33.3 ms (30 FPS floor)
- p95 frame time: <= 25 ms (60 target) or <= 40 ms (30 floor)
- Active memory growth over 20 minutes guidance: <= 10%
- Thermal throttling events: 0 during standard 10-step scenario
- Reconnect recovery time after network restore: <= 5 s

## Measurement Procedure
1. Build and deploy a release-like client.
2. Start server endpoints (HTTP + gRPC if used).
3. Run one warmup loop through first 2 steps.
4. Run full pilot scenario while recording:
- Unity profiler stats (frame time, GC allocations, memory).
- Device thermal/perf counters from vendor tooling.
- App diagnostics export (`SessionStatusPanel` diagnostics action).
5. Repeat with network interruption at least once.

## Pass/Fail Criteria
- Pass: all budget targets met in 3 consecutive runs.
- Conditional pass: minor single-metric overrun with mitigation documented.
- Fail: repeated frame drops, thermal throttling, or guidance-loop breakage.

## Mitigation Playbook
- Frame-time high:
- Reduce model complexity and texture resolution in exported GLB.
- Verify one-active-model rule is preserved.
- Memory growth:
- Check model disposal path in `ModelPresenter` and asset cache retention strategy.
- Thermal pressure:
- Lower rendering quality preset and reduce expensive post-processing.
- Reconnect lag:
- Tune reconnect backoff bounds in `AppBootstrap`.

## Run Record (Fill Per Device)
| Run ID | Device | Build | Median ms | p95 ms | Memory delta | Thermal events | Reconnect recovery | Result | Evidence Path |
|---|---|---|---:|---:|---:|---:|---:|---|---|
| PTB-001 | Pending | Pending |  |  |  |  |  | Pending | `shared/fixtures/pilot-evidence/PW-01/` |
| PTB-002 | Pending | Pending |  |  |  |  |  | Pending | `shared/fixtures/pilot-evidence/PW-03/` |
| PTB-003 | Pending | Pending |  |  |  |  |  | Pending | `shared/fixtures/pilot-evidence/PW-05/` |
