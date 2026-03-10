from pathlib import Path
import json
import sys

from fastapi.testclient import TestClient

sys.path.append(str(Path(__file__).resolve().parents[1] / "app"))

from config import AppConfig
from server_kit_main import create_app


def test_package_build_endpoint_generates_manifest_and_assets(tmp_path: Path) -> None:
    source_manifest_root = tmp_path / "source" / "manifests"
    source_asset_root = tmp_path / "source" / "assets" / "sha256_source"
    output_manifest_root = tmp_path / "out" / "manifests"
    output_asset_root = tmp_path / "out" / "assets"
    target_root = tmp_path / "source" / "targets"
    steps_file = tmp_path / "source" / "step-definitions.yaml"

    source_manifest_root.mkdir(parents=True, exist_ok=True)
    source_asset_root.mkdir(parents=True, exist_ok=True)
    target_root.mkdir(parents=True, exist_ok=True)
    steps_file.write_text("workflowVersion: 1.0.0\njobs: []\n", encoding="utf-8")

    (source_manifest_root / "job-http-001.manifest.json").write_text(
      json.dumps(
        {
          "jobId": "job-http-001",
          "workflowVersion": "1.0.0",
          "steps": [
            {
              "stepId": "17",
              "partId": "Bracket_12",
              "assetVersion": "sha256_source",
              "glbFile": "part.glb",
              "stepJsonFile": "step_17.json",
              "targetVersion": "2026-03-10.1",
              "targetFile": "AssemblyMarker_A.dat",
              "compression": "NONE",
            }
          ],
        }
      ),
      encoding="utf-8",
    )
    (source_asset_root / "part.glb").write_bytes(b"GLB_BYTES")
    (source_asset_root / "step_17.json").write_text(
      json.dumps({"stepId": "17", "displayName": "Install bracket"}),
      encoding="utf-8",
    )

    app = create_app(
      AppConfig(
        manifests_root=source_manifest_root,
        asset_root=tmp_path / "source" / "assets",
        target_root=target_root,
        export_manifest_root=output_manifest_root,
        export_asset_root=output_asset_root,
        step_definition_file=steps_file,
        draco_enabled=False,
      )
    )
    client = TestClient(app)

    response = client.post("/api/jobs/job-http-001/packages:build")
    assert response.status_code == 202

    body = response.json()
    assert body["jobId"] == "job-http-001"
    assert body["runId"]
    assert body["state"] in {"queued", "running", "succeeded"}

    status_response = client.get(body["statusUrl"])
    assert status_response.status_code == 200
    status_payload = status_response.json()
    assert status_payload["jobId"] == "job-http-001"
    assert status_payload["state"] == "succeeded"
    assert status_payload["generatedSteps"] == 1

    output_manifest = output_manifest_root / "job-http-001.manifest.json"
    assert output_manifest.exists()
    manifest_payload = json.loads(output_manifest.read_text(encoding="utf-8"))
    assert manifest_payload["steps"][0]["compression"] == "NONE"
    assert manifest_payload["steps"][0]["assetVersion"].startswith("sha256_")
