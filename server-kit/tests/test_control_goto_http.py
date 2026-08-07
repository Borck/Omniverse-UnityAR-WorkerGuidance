"""The dashboard's step-click path: POST /control/jobs/{job}/goto/{step} must
build a GOTO ControlStepRequest and surface the gRPC response. Patched at the
grpc module boundary so no sibling gRPC process is needed."""

from contextlib import contextmanager
from pathlib import Path
import sys

import grpc
from fastapi.testclient import TestClient

sys.path.append(str(Path(__file__).resolve().parents[1] / "app"))
sys.path.append(str(Path(__file__).resolve().parents[1] / "app" / "generated"))

from app.generated import guidance_pb2, guidance_pb2_grpc
from config import AppConfig
from server_kit_main import create_app


class _FakeStub:
    """Records the request it was handed and replays a canned response."""

    last_request = None

    def __init__(self, channel, ok=True, notified=2):
        self._ok, self._notified = ok, notified

    def ControlStep(self, request, timeout=None):
        _FakeStub.last_request = request
        return guidance_pb2.ControlStepResponse(
            ok=self._ok,
            activated_step_id=request.step_id,
            sessions_notified=self._notified,
            message="pushed" if self._ok else "step not found",
        )


@contextmanager
def _noop_channel(_target):
    yield object()


def _patch_grpc(monkeypatch, stub_factory):
    monkeypatch.setattr(grpc, "insecure_channel", _noop_channel)
    monkeypatch.setattr(grpc, "channel_ready_future", lambda ch: type("F", (), {"result": lambda self, timeout=None: None})())
    monkeypatch.setattr(guidance_pb2_grpc, "GuidanceControlServiceStub", stub_factory)


def _client(tmp_path: Path) -> TestClient:
    return TestClient(create_app(AppConfig(session_store_file=Path(tmp_path / "sessions.json"))))


def test_goto_sends_goto_action_and_reports_notified(monkeypatch, tmp_path: Path) -> None:
    _patch_grpc(monkeypatch, _FakeStub)
    response = _client(tmp_path).post("/control/jobs/Segment_Assembly/goto/step-5")

    assert response.status_code == 200
    body = response.json()
    assert body == {
        "ok": True,
        "activated_step_id": "step-5",
        "sessions_notified": 2,
        "message": "pushed",
    }
    sent = _FakeStub.last_request
    assert sent.job_id == "Segment_Assembly"
    assert sent.step_id == "step-5"
    assert sent.action == guidance_pb2.CONTROL_ACTION_GOTO


def test_goto_unknown_step_is_404(monkeypatch, tmp_path: Path) -> None:
    _patch_grpc(monkeypatch, lambda channel: _FakeStub(channel, ok=False, notified=0))
    response = _client(tmp_path).post("/control/jobs/Segment_Assembly/goto/nope")

    assert response.status_code == 404
    assert response.json()["detail"] == "step not found"


def test_goto_is_502_when_grpc_process_is_down(monkeypatch, tmp_path: Path) -> None:
    def _refuse(_target):
        raise RuntimeError("connection refused")

    monkeypatch.setattr(grpc, "insecure_channel", _refuse)
    response = _client(tmp_path).post("/control/jobs/j/goto/s")

    assert response.status_code == 502
