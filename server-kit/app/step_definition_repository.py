from dataclasses import dataclass
from pathlib import Path

import yaml


@dataclass(frozen=True)
class StepDefinition:
    job_id: str
    step_id: str
    part_id: str
    display_name: str
    source_prim_path: str
    animation_name: str
    anchor_type: str
    target_id: str
    target_version: str
    asset_version: str
    instructions_short: str
    safety_notes: list[str]
    expected_duration_sec: int


class StepDefinitionRepository:
    def __init__(self, step_definition_file: Path) -> None:
        self._step_definition_file = step_definition_file

    def get_steps(self, job_id: str) -> list[StepDefinition]:
        payload = yaml.safe_load(self._step_definition_file.read_text(encoding="utf-8"))
        jobs = payload.get("jobs", [])

        for job in jobs:
            if job.get("jobId") != job_id:
                continue
            return [
                StepDefinition(
                    job_id=job_id,
                    step_id=item["stepId"],
                    part_id=item["partId"],
                    display_name=item["displayName"],
                    source_prim_path=item["sourcePrimPath"],
                    animation_name=item["animationName"],
                    anchor_type=item["anchorType"],
                    target_id=item["targetId"],
                    target_version=item["targetVersion"],
                    asset_version=item["assetVersion"],
                    instructions_short=item["instructionsShort"],
                    safety_notes=item.get("safetyNotes", []),
                    expected_duration_sec=int(item.get("expectedDurationSec", 0)),
                )
                for item in job.get("steps", [])
            ]
        return []
