from pathlib import Path
import sys

sys.path.append(str(Path(__file__).resolve().parents[1] / "app"))

from fastapi.testclient import TestClient

from server_kit_main import app


def test_health_endpoint_returns_ok() -> None:
    client = TestClient(app)
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {"status": "ok"}
