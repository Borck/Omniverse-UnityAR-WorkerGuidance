"""Loads canonical step definitions from YAML into typed runtime records."""

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
    sequence_index: int
    timeline_start_step: int
    timeline_end_step: int
    timeline_fps: int
    animation_start_step: int
    animation_end_step: int
    keep_visible_until_step: int
    active_prim_path: str
    animation_layer_role: str
    target_layer_role: str
    start_offset_xyz: tuple[float, float, float]
    target_position_xyz: tuple[float, float, float]


class StepDefinitionRepository:
    """Provides job-scoped step definitions used by session and resolver services."""

    def __init__(self, step_definition_file: Path) -> None:
        self._step_definition_file = step_definition_file

    def get_steps(self, job_id: str) -> list[StepDefinition]:
        """Returns ordered raw step definitions for one job id."""
        payload = yaml.safe_load(self._step_definition_file.read_text(encoding="utf-8"))
        jobs = payload.get("jobs", [])

        for job in jobs:
            if job.get("jobId") != job_id:
                continue
            timeline_profile = job.get("timelineProfile", {})
            timeline_start_step = int(timeline_profile.get("startStep", 0))
            timeline_end_step = int(timeline_profile.get("endStep", 0))
            timeline_fps = int(timeline_profile.get("fps", 0))
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
                    sequence_index=int(item.get("sequenceIndex", idx + 1)),
                    timeline_start_step=timeline_start_step,
                    timeline_end_step=timeline_end_step,
                    timeline_fps=timeline_fps,
                    animation_start_step=int(item.get("animationStartStep", 0)),
                    animation_end_step=int(item.get("animationEndStep", 0)),
                    keep_visible_until_step=int(item.get("keepVisibleUntilStep", 0)),
                    active_prim_path=str(item.get("activePrimPath", item["sourcePrimPath"])),
                    animation_layer_role=str(item.get("animationLayerRole", "animation")),
                    target_layer_role=str(item.get("targetLayerRole", "target-position")),
                    start_offset_xyz=self._parse_vector3(item.get("startOffset", [0.0, 0.1, 0.0])),
                    target_position_xyz=self._parse_vector3(item.get("targetPosition", [0.0, 0.0, 0.0])),
                )
                for idx, item in enumerate(job.get("steps", []))
            ]
        return []

    @staticmethod
    def _parse_vector3(raw: list[float] | tuple[float, ...]) -> tuple[float, float, float]:
        if not isinstance(raw, (list, tuple)) or len(raw) != 3:
            return (0.0, 0.0, 0.0)
        return (float(raw[0]), float(raw[1]), float(raw[2]))
