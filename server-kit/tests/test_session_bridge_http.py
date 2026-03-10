from pathlib import Path
import sys

from fastapi.testclient import TestClient

sys.path.append(str(Path(__file__).resolve().parents[1] / "app"))

from config import AppConfig
from server_kit_main import create_app


def _make_client_with_session_store(tmp_path: Path) -> TestClient:
    config = AppConfig(session_store_file=Path(tmp_path / "sessions.json"))
    app = create_app(config)
    return TestClient(app)


def test_session_connect_returns_hello_and_step_payload(tmp_path: Path) -> None:
    client = _make_client_with_session_store(tmp_path)

    response = client.post(
        "/session/connect",
        json={
            "hello": {
                "device_id": "device-http-1",
                "app_version": "0.1.0",
                "capabilities": "unity-ar",
            }
        },
    )

    assert response.status_code == 200
    body = response.json()
    assert body["hello_response"]["session_id"] == "session-1"
    assert body["hello_response"]["protocol_version"] == "v1"
    assert body["step_activated"]["job_id"] == "job-mock-001"
    assert body["step_activated"]["step_id"] == "17"


def test_session_heartbeat_returns_ping_for_known_session(tmp_path: Path) -> None:
    client = _make_client_with_session_store(tmp_path)

    connect = client.post(
        "/session/connect",
        json={
            "hello": {
                "device_id": "device-http-2",
                "app_version": "0.1.0",
                "capabilities": "unity-ar",
            }
        },
    )
    session_id = connect.json()["hello_response"]["session_id"]

    heartbeat = client.post(
        "/session/heartbeat",
        json={
            "heartbeat": {
                "session_id": session_id,
                "client_time_unix_ms": 123456,
            }
        },
    )

    assert heartbeat.status_code == 200
    assert heartbeat.json()["ping"]["nonce"] == "hb-123456"


def test_session_heartbeat_returns_fault_for_unknown_session(tmp_path: Path) -> None:
    client = _make_client_with_session_store(tmp_path)

    heartbeat = client.post(
        "/session/heartbeat",
        json={
            "heartbeat": {
                "session_id": "session-does-not-exist",
                "client_time_unix_ms": 99,
            }
        },
    )

    assert heartbeat.status_code == 404
    fault = heartbeat.json()["fault"]
    assert fault["code"] == "SESSION_NOT_FOUND"
    assert fault["recoverable"] is True


def test_session_connect_reuses_session_for_same_device(tmp_path: Path) -> None:
    client = _make_client_with_session_store(tmp_path)

    first = client.post(
        "/session/connect",
        json={
            "hello": {
                "device_id": "device-http-resume",
                "app_version": "0.1.0",
                "capabilities": "unity-ar",
            }
        },
    )
    second = client.post(
        "/session/connect",
        json={
            "hello": {
                "device_id": "device-http-resume",
                "app_version": "0.1.1",
                "capabilities": "unity-ar",
            }
        },
    )

    assert first.status_code == 200
    assert second.status_code == 200
    assert first.json()["hello_response"]["session_id"] == second.json()["hello_response"]["session_id"]
