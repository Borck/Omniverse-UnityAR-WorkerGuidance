from pathlib import Path
import json
import sys

sys.path.append(str(Path(__file__).resolve().parents[1]))

from app.export_pipeline import StepPackageExporter
from app.glb_exporter import OmniverseStageGlbExporter
from app.manifest_service import ManifestService


class FakeDracoCodec:
    def __init__(self, enabled: bool) -> None:
        self._enabled = enabled

    def encode_to_draco(self, source_glb: Path, target_glb: Path) -> bool:
        if not self._enabled:
            return False
        target_glb.write_bytes(b"DRACO" + source_glb.read_bytes())
        return True


class PrefixGlbExporter:
    def __init__(self, prefix: bytes) -> None:
        self._prefix = prefix

    def export_glb(self, source_glb: Path, output_glb: Path) -> bool:
        output_glb.write_bytes(self._prefix + source_glb.read_bytes())
        return True


def _write_source_fixture(root: Path) -> tuple[Path, Path]:
    manifest_root = root / "manifests"
    asset_root = root / "assets" / "sha256_source"
    manifest_root.mkdir(parents=True, exist_ok=True)
    asset_root.mkdir(parents=True, exist_ok=True)

    (manifest_root / "job-mock-001.manifest.json").write_text(
        json.dumps(
            {
                "jobId": "job-mock-001",
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
    (asset_root / "part.glb").write_bytes(b"GLB_BYTES")
    (asset_root / "step_17.json").write_text(
        json.dumps({"stepId": "17", "displayName": "Install bracket"}),
        encoding="utf-8",
    )

    return manifest_root, root / "assets"


def test_export_pipeline_uses_draco_when_available(tmp_path: Path) -> None:
    manifest_root, source_asset_root = _write_source_fixture(tmp_path / "source")
    output_manifest_root = tmp_path / "output" / "manifests"
    output_asset_root = tmp_path / "output" / "assets"

    exporter = StepPackageExporter(
        manifest_service=ManifestService(manifest_root),
        source_asset_root=source_asset_root,
        output_asset_root=output_asset_root,
        output_manifest_root=output_manifest_root,
        draco_codec=FakeDracoCodec(enabled=True),
    )

    result = exporter.build_job_packages("job-mock-001")
    manifest = json.loads(result.manifest_path.read_text(encoding="utf-8"))

    assert result.generated_steps == 1
    assert manifest["steps"][0]["compression"] == "DRACO"
    version_dir = output_asset_root / manifest["steps"][0]["assetVersion"]
    glb_path = version_dir / manifest["steps"][0]["glbFile"]
    assert glb_path.read_bytes().startswith(b"DRACO")


def test_export_pipeline_falls_back_to_none_when_draco_unavailable(tmp_path: Path) -> None:
    manifest_root, source_asset_root = _write_source_fixture(tmp_path / "source")
    output_manifest_root = tmp_path / "output" / "manifests"
    output_asset_root = tmp_path / "output" / "assets"

    exporter = StepPackageExporter(
        manifest_service=ManifestService(manifest_root),
        source_asset_root=source_asset_root,
        output_asset_root=output_asset_root,
        output_manifest_root=output_manifest_root,
        draco_codec=FakeDracoCodec(enabled=False),
    )

    result = exporter.build_job_packages("job-mock-001")
    manifest = json.loads(result.manifest_path.read_text(encoding="utf-8"))

    assert result.generated_steps == 1
    assert manifest["steps"][0]["compression"] == "NONE"
    version_dir = output_asset_root / manifest["steps"][0]["assetVersion"]
    glb_path = version_dir / manifest["steps"][0]["glbFile"]
    assert glb_path.read_bytes() == b"GLB_BYTES"


def test_export_pipeline_is_reproducible_for_identical_inputs(tmp_path: Path) -> None:
    manifest_root, source_asset_root = _write_source_fixture(tmp_path / "source")
    output_manifest_root = tmp_path / "output" / "manifests"
    output_asset_root = tmp_path / "output" / "assets"

    exporter = StepPackageExporter(
        manifest_service=ManifestService(manifest_root),
        source_asset_root=source_asset_root,
        output_asset_root=output_asset_root,
        output_manifest_root=output_manifest_root,
        draco_codec=FakeDracoCodec(enabled=False),
    )

    first = exporter.build_job_packages("job-mock-001")
    second = exporter.build_job_packages("job-mock-001")
    first_manifest = json.loads(first.manifest_path.read_text(encoding="utf-8"))
    second_manifest = json.loads(second.manifest_path.read_text(encoding="utf-8"))

    assert first_manifest["steps"][0]["assetVersion"] == second_manifest["steps"][0]["assetVersion"]
    assert first_manifest["steps"][0]["glbFile"] == second_manifest["steps"][0]["glbFile"]


def test_export_pipeline_uses_glb_export_backend_before_draco(tmp_path: Path) -> None:
    manifest_root, source_asset_root = _write_source_fixture(tmp_path / "source")
    output_manifest_root = tmp_path / "output" / "manifests"
    output_asset_root = tmp_path / "output" / "assets"

    exporter = StepPackageExporter(
        manifest_service=ManifestService(manifest_root),
        source_asset_root=source_asset_root,
        output_asset_root=output_asset_root,
        output_manifest_root=output_manifest_root,
        draco_codec=FakeDracoCodec(enabled=False),
        glb_exporter=PrefixGlbExporter(prefix=b"EXPORTED_"),
    )

    result = exporter.build_job_packages("job-mock-001")
    manifest = json.loads(result.manifest_path.read_text(encoding="utf-8"))
    version_dir = output_asset_root / manifest["steps"][0]["assetVersion"]
    glb_path = version_dir / manifest["steps"][0]["glbFile"]

    assert glb_path.read_bytes() == b"EXPORTED_GLB_BYTES"


def test_omniverse_glb_exporter_default_output_path_for_stage_url() -> None:
    output = OmniverseStageGlbExporter.derive_default_output_path(
        "omniverse://localhost/Projects/Assembly.usd",
        step_number=3,
    )
    assert output == "omniverse://localhost/Projects/Export_GLB/Assembly_step3.glb"
