from pathlib import Path
import sys

from fastapi.testclient import TestClient

sys.path.append(str(Path(__file__).resolve().parents[1]))

from app.config import AppConfig
from app.server_kit_main import create_app


def test_layers_resolve_returns_btu_style_visibility_projection() -> None:
    app = create_app(AppConfig())
    client = TestClient(app)

    response = client.post(
        "/api/jobs/job-layer-example-001/layers:resolve",
        json={
            "sublayer_paths_bottom_to_top": [
                "PART_A_anim.usd",
                "PART_A-Position.usd",
                "PART_B_anim.usd",
                "PART_B-Position.usd",
                "PART_C_anim.usd",
                "PART_C-Position.usd",
                "PART_D_anim.usd",
                "PART_D-Position.usd",
                "PART_E_anim.usd",
                "PART_E-Position.usd",
            ]
        },
    )

    assert response.status_code == 200
    payload = response.json()
    assert payload["jobId"] == "job-layer-example-001"
    assert len(payload["resolvedSteps"]) == 5

    step1 = payload["resolvedSteps"][0]
    assert step1["stepId"] == "10"
    assert step1["visibleLayerIds"] == ["PART_A-Position.usd", "PART_B_anim.usd"]

    step5 = payload["resolvedSteps"][4]
    assert step5["stepId"] == "50"
    assert step5["visibleLayerIds"] == [
        "PART_A-Position.usd",
        "PART_B-Position.usd",
        "PART_C-Position.usd",
        "PART_D-Position.usd",
        "PART_E-Position.usd",
    ]


def test_layers_resolve_returns_empty_list_for_unknown_job() -> None:
    app = create_app(AppConfig())
    client = TestClient(app)

    response = client.post(
        "/api/jobs/job-does-not-exist/layers:resolve",
        json={"sublayer_paths_bottom_to_top": ["A.usd", "A-Position.usd"]},
    )

    assert response.status_code == 200
    payload = response.json()
    assert payload["jobId"] == "job-does-not-exist"
    assert payload["resolvedSteps"] == []
