from pathlib import Path
import sys

from fastapi.testclient import TestClient

sys.path.append(str(Path(__file__).resolve().parents[1] / "app"))

from server_kit_main import app


def test_manifest_endpoint_returns_fixture_with_cache_headers() -> None:
    client = TestClient(app)
    response = client.get("/api/jobs/job-mock-001/manifest")

    assert response.status_code == 200
    body = response.json()
    assert body["jobId"] == "job-mock-001"
    assert len(body["steps"]) == 1
    assert response.headers["cache-control"].startswith("public")
    assert "etag" in response.headers
