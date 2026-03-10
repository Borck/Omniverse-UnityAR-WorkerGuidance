from pathlib import Path
import sys

from fastapi.testclient import TestClient

sys.path.append(str(Path(__file__).resolve().parents[1] / "app"))

from server_kit_main import app


def test_asset_and_target_endpoints_return_immutable_headers() -> None:
    client = TestClient(app)

    asset_response = client.get("/api/assets/sha256_18c7dd53a481165d/part_Bracket_12_18c7dd53.glb")
    assert asset_response.status_code == 200
    assert "immutable" in asset_response.headers["cache-control"]
    assert "etag" in asset_response.headers

    target_response = client.get("/api/targets/2026-03-10.1/AssemblyMarker_A.dat")
    assert target_response.status_code == 200
    assert "immutable" in target_response.headers["cache-control"]
    assert "etag" in target_response.headers
