# Integration Test Matrix (M12)

Updated: 2026-03-10

This matrix defines the minimum integration coverage required before pilot rollout.

## Scope
- Server HTTP API and gRPC session behavior
- Export pipeline and manifest resolution
- Unity runtime state flow (manual and editor-assisted)
- Network degradation and recoverability

## Matrix

| ID | Area | Environment | Command / Procedure | Expected Result |
|---|---|---|---|---|
| IT-01 | Server health | Windows dev machine | `.\.venv\Scripts\python.exe -m pytest -c server-kit/pytest.ini server-kit/tests/test_health.py -q` | Health test passes |
| IT-02 | Session bridge HTTP | Windows dev machine | `.\.venv\Scripts\python.exe -m pytest -c server-kit/pytest.ini server-kit/tests/test_session_bridge_http.py -q` | Connect/heartbeat/step-completed behavior passes |
| IT-03 | gRPC session flow | Windows dev machine | `.\.venv\Scripts\python.exe -m pytest -c server-kit/pytest.ini server-kit/tests/test_grpc_session_stub.py -q` | Hello/heartbeat/step progression passes |
| IT-04 | Resolver determinism | Windows dev machine | `.\.venv\Scripts\python.exe -m pytest -c server-kit/pytest.ini server-kit/tests/test_layer_stack_resolver.py -q` | Fixed fixture -> fixed hash and layer projection |
| IT-05 | Export reproducibility | Windows dev machine | `.\.venv\Scripts\python.exe -m pytest -c server-kit/pytest.ini server-kit/tests/test_export_pipeline.py -q` | Stable package materialization across runs |
| IT-06 | Full backend suite | Windows dev machine | `.\.venv\Scripts\python.exe -m pytest -c server-kit/pytest.ini server-kit/tests -q` | No regressions in server-kit test suite |
| IT-07 | Proto generation parity | Windows dev machine | `dotnet build tools/proto-csharp/ProtoCSharpGen.csproj -nologo -v minimal` + Python protoc command from `README.md` | Python and C# contract generation both succeed |
| IT-08 | Unity editor state machine | Unity 6 editor | Run EditMode tests under `Assets/App/Tests/Editor` | Step coordinator and memory probe tests pass |
| IT-09 | Runtime connect/reconnect | Android target device | Start app, toggle network off/on during active step | Frozen-step warning during outage, recovery after reconnect |
| IT-10 | End-to-end step loop | Android target device + local server | Complete full configured sequence with `confirm` actions | Every step transitions, no stuck state, no crash |

## Fast Execution
Use `tools/scripts/run-validation-matrix.ps1` to execute automated backend checks (`IT-01` through `IT-07`).

Manual cases (`IT-08` to `IT-10`) must be recorded in the pilot validation sheet (`docs/validation/pilot-workflows-e2e.md`).
