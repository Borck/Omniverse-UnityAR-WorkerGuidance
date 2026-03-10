from pathlib import Path
import json
import sys
import pytest

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
        export_job_store_file=tmp_path / "runtime" / "export-jobs.json",
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


def test_package_build_endpoint_enqueue_only_mode_leaves_job_queued(tmp_path: Path) -> None:
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

    (source_manifest_root / "job-http-002.manifest.json").write_text(
      json.dumps(
        {
          "jobId": "job-http-002",
          "workflowVersion": "1.0.0",
          "steps": [
            {
              "stepId": "18",
              "partId": "Bracket_13",
              "assetVersion": "sha256_source",
              "glbFile": "part.glb",
              "stepJsonFile": "step_18.json",
              "targetVersion": "2026-03-10.1",
              "targetFile": "AssemblyMarker_B.dat",
              "compression": "NONE",
            }
          ],
        }
      ),
      encoding="utf-8",
    )
    (source_asset_root / "part.glb").write_bytes(b"GLB_BYTES")
    (source_asset_root / "step_18.json").write_text(
      json.dumps({"stepId": "18", "displayName": "Install bracket"}),
      encoding="utf-8",
    )

    app = create_app(
      AppConfig(
        manifests_root=source_manifest_root,
        asset_root=tmp_path / "source" / "assets",
        target_root=target_root,
        export_manifest_root=output_manifest_root,
        export_asset_root=output_asset_root,
        export_job_store_file=tmp_path / "runtime" / "export-jobs.json",
        export_job_processing_mode="enqueue-only",
        step_definition_file=steps_file,
        draco_enabled=False,
      )
    )
    client = TestClient(app)

    response = client.post("/api/jobs/job-http-002/packages:build")
    assert response.status_code == 202

    body = response.json()
    assert body["jobId"] == "job-http-002"
    assert body["runId"]
    assert body["state"] == "queued"
    assert body["processingMode"] == "enqueue-only"

    status_response = client.get(body["statusUrl"])
    assert status_response.status_code == 200
    status_payload = status_response.json()
    assert status_payload["state"] == "queued"
    assert status_payload["manifestPath"] is None

    output_manifest = output_manifest_root / "job-http-002.manifest.json"
    assert not output_manifest.exists()


def test_create_app_rejects_invalid_export_processing_mode() -> None:
    with pytest.raises(ValueError, match="GUIDANCE_EXPORT_JOB_PROCESSING_MODE"):
      create_app(AppConfig(export_job_processing_mode="invalid-mode"))
