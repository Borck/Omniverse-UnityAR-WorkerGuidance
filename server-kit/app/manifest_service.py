from dataclasses import dataclass
import json
from pathlib import Path


@dataclass(frozen=True)
class StepAssetEntry:
    step_id: str
    part_id: str
    asset_version: str
    glb_file: str
    step_json_file: str
    target_version: str
    target_file: str
    compression: str


@dataclass(frozen=True)
class ManifestModel:
    job_id: str
    workflow_version: str
    steps: list[StepAssetEntry]


class ManifestService:
    def __init__(self, manifests_root: Path) -> None:
        self._manifests_root = manifests_root

    def get_manifest(self, job_id: str) -> ManifestModel:
        path = self._manifests_root / f"{job_id}.manifest.json"
        payload = json.loads(path.read_text(encoding="utf-8"))
        steps = [
            StepAssetEntry(
                step_id=item["stepId"],
                part_id=item["partId"],
                asset_version=item["assetVersion"],
                glb_file=item["glbFile"],
                step_json_file=item["stepJsonFile"],
                target_version=item["targetVersion"],
                target_file=item["targetFile"],
                compression=item.get("compression", "NONE"),
            )
            for item in payload["steps"]
        ]
        return ManifestModel(
            job_id=payload["jobId"],
            workflow_version=payload["workflowVersion"],
            steps=steps,
        )
