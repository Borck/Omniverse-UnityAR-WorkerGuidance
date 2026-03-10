# Release Checklist And Failure Playbooks (M12)

Updated: 2026-03-10

## Release Checklist
- [ ] Integration matrix automated cases (`IT-01`..`IT-07`) are green.
- [ ] Manual matrix cases (`IT-08`..`IT-10`) recorded and approved.
- [ ] Pilot workflows (`PW-01`..`PW-05`) executed with evidence attached.
- [ ] Performance and thermal budget validated on target device(s).
- [ ] Proto generation reproducible from clean checkout.
- [ ] HTTP runtime API notes and contract docs are up to date.
- [ ] ADRs updated for any behavior changes.
- [ ] Rollback procedure rehearsed (previous server image + previous client build).

## Failure Playbooks

### FP-01 Session Cannot Connect
1. Verify server health endpoint (`GET /health`).
2. Verify correct transport selection (gRPC target or HTTP base URL).
3. Check firewall/VPN constraints.
4. Fallback to HTTP bridge if gRPC path is blocked.
5. Collect diagnostics bundle and server logs.

### FP-02 Step Confirm Does Not Advance
1. Check `step_completed` request/stream emission from Unity.
2. Verify server step-definition ordering for active job.
3. Confirm idempotency cache did not treat the event as duplicate incorrectly.
4. Trigger `replay` and re-run `confirm` once.
5. If still blocked, reset session and annotate issue with `session_id`.

### FP-03 Tracking Lost For Extended Period
1. Ensure target payload is present and activated for current step.
2. Use directional hint to reacquire target.
3. Keep current step frozen until tracking is reacquired.
4. If unresolved, use operator `help` and restart from previous stable step.

### FP-04 Performance Degradation On Device
1. Inspect memory growth and frame-time metrics.
2. Validate that inactive model instances are disposed.
3. Reduce rendered complexity for test rerun.
4. Re-run same workflow and compare diagnostics.

### FP-05 Export Package Build Fails
1. Check package job status endpoint.
2. Validate source GLB and step JSON availability.
3. Verify Draco toolchain config and fallback behavior.
4. Retry with `GUIDANCE_DRACO_ENABLED=false` for isolation.
