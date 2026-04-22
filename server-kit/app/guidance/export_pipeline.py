"""Step package export pipeline that emits deterministic runtime assets."""

from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
from tempfile import TemporaryDirectory
from typing import Protocol

from app.assets.glb import GlbExportBackend, PassthroughGlbExporter
from app.guidance.manifest_service import ManifestService


class CompressionCodec(Protocol):
    def encode_to_draco(self, source_glb: Path, target_glb: Path) -> bool:
        ...


@dataclass(frozen=True)
class ExportResult:
    manifest_path: Path
    generated_steps: int


class StepPackageExporter:
    """Builds versioned per-step package outputs from source manifests and assets."""

    def __init__(
        self,
        manifest_service: ManifestService,
        source_asset_root: Path,
        output_asset_root: Path,
        output_manifest_root: Path,
        draco_codec: CompressionCodec,
        glb_exporter: GlbExportBackend | None = None,
    ) -> None:
        self._manifest_service = manifest_service
        self._source_asset_root = source_asset_root
        self._output_asset_root = output_asset_root
        self._output_manifest_root = output_manifest_root
        self._draco_codec = draco_codec
        self._glb_exporter = glb_exporter or PassthroughGlbExporter()

    def build_job_packages(self, job_id: str) -> ExportResult:
        """Exports all steps for one job and writes a generated manifest."""
        source_manifest = self._manifest_service.get_manifest(job_id)

        generated_steps: list[dict[str, str]] = []
        for step in source_manifest.steps:
            source_dir = self._source_asset_root / step.asset_version
            source_glb = source_dir / step.glb_file
            source_step_json = source_dir / step.step_json_file

            if not source_glb.exists():
                raise FileNotFoundError(f"Missing source GLB: {source_glb}")
            if not source_step_json.exists():
                raise FileNotFoundError(f"Missing source step JSON: {source_step_json}")

            glb_bytes, compression = self._materialize_glb(source_glb)
            step_json_payload = json.loads(source_step_json.read_text(encoding="utf-8"))

            digest = self._hash_payload(glb_bytes, json.dumps(step_json_payload, sort_keys=True).encode("utf-8"))
            digest_short = digest[:16]
            asset_version = f"sha256_{digest_short}"
            glb_file = f"part_{step.part_id}_{digest[:8]}.glb"

            output_dir = self._output_asset_root / asset_version
            output_dir.mkdir(parents=True, exist_ok=True)
            (output_dir / glb_file).write_bytes(glb_bytes)

            step_json_payload["assetVersion"] = asset_version
            step_json_payload["compression"] = compression
            step_json_payload["glbFile"] = glb_file
            (output_dir / step.step_json_file).write_text(
                json.dumps(step_json_payload, indent=2),
                encoding="utf-8",
            )

            generated_steps.append(
                {
                    "stepId": step.step_id,
                    "partId": step.part_id,
                    "assetVersion": asset_version,
                    "glbFile": glb_file,
                    "stepJsonFile": step.step_json_file,
                    "targetVersion": step.target_version,
                    "targetFile": step.target_file,
                    "compression": compression,
                }
            )

        manifest_payload = {
            "jobId": source_manifest.job_id,
            "workflowVersion": source_manifest.workflow_version,
            "steps": generated_steps,
        }
        self._output_manifest_root.mkdir(parents=True, exist_ok=True)
        manifest_path = self._output_manifest_root / f"{job_id}.manifest.json"
        manifest_path.write_text(json.dumps(manifest_payload, indent=2), encoding="utf-8")

        return ExportResult(manifest_path=manifest_path, generated_steps=len(generated_steps))

    def _materialize_glb(self, source_glb: Path) -> tuple[bytes, str]:
        with TemporaryDirectory(prefix="guidance-export-") as temp_dir:
            exported_glb = Path(temp_dir) / f"exported_{source_glb.name}"
            if not self._glb_exporter.export_glb(source_glb, exported_glb):
                raise RuntimeError(f"GLB export backend failed for {source_glb}")

            encoded_glb = Path(temp_dir) / f"draco_{source_glb.name}"
            if self._draco_codec.encode_to_draco(exported_glb, encoded_glb):
                return encoded_glb.read_bytes(), "DRACO"
            return exported_glb.read_bytes(), "NONE"

    @staticmethod
    def _hash_payload(glb_bytes: bytes, step_json_bytes: bytes) -> str:
        hasher = hashlib.sha256()
        hasher.update(glb_bytes)
        hasher.update(step_json_bytes)
        return hasher.hexdigest()
