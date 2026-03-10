from pathlib import Path
import sys

from fastapi.testclient import TestClient

sys.path.append(str(Path(__file__).resolve().parents[1] / "app"))

from config import AppConfig
from server_kit_main import create_app


def test_stage_open_smoke_returns_conflict_when_not_configured() -> None:
    app = create_app(AppConfig(stage_uri=""))
    client = TestClient(app)

    response = client.post("/api/stage:open-smoke")

    assert response.status_code == 409
    payload = response.json()
    assert payload["success"] is False
    assert payload["code"] == "STAGE_URI_NOT_CONFIGURED"


def test_stage_open_smoke_returns_ok_for_valid_omniverse_uri() -> None:
    app = create_app(AppConfig(stage_uri="omniverse://localhost/Projects/Assembly.usd"))
    client = TestClient(app)

    response = client.post("/api/stage:open-smoke")

    assert response.status_code == 200
    payload = response.json()
    assert payload["success"] is True
    assert payload["code"] == "STAGE_OPEN_SMOKE_OK"
    assert payload["stageUri"] == "omniverse://localhost/Projects/Assembly.usd"


def test_stage_open_smoke_returns_bad_request_for_invalid_scheme() -> None:
    app = create_app(AppConfig(stage_uri="ftp://localhost/Projects/Assembly.usd"))
    client = TestClient(app)

    response = client.post("/api/stage:open-smoke")

    assert response.status_code == 400
    payload = response.json()
    assert payload["success"] is False
    assert payload["code"] == "STAGE_URI_INVALID"
