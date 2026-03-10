from pathlib import Path
import sys

sys.path.append(str(Path(__file__).resolve().parents[1] / "app"))

from step_definition_repository import StepDefinitionRepository


def test_step_definition_repository_parses_layer_timeline_metadata() -> None:
    repo = StepDefinitionRepository(
        step_definition_file=Path("shared/samples/step-definitions.yaml")
    )

    steps = repo.get_steps("job-layer-example-001")

    assert len(steps) == 5
    first = steps[0]
    assert first.part_id == "PART_A"
    assert first.sequence_index == 1
    assert first.animation_start_step == 1
    assert first.animation_end_step == 10
    assert first.keep_visible_until_step == 101
    assert first.animation_layer_role == "animation"
    assert first.target_layer_role == "target-position"
    assert first.start_offset_xyz == (0.0, 0.1, 0.0)
    assert first.target_position_xyz == (0.0, 0.0, 0.0)

    last = steps[-1]
    assert last.part_id == "PART_E"
    assert last.sequence_index == 5
    assert last.animation_start_step == 51
    assert last.animation_end_step == 60


def test_step_definition_repository_backfills_defaults_for_legacy_minimal_item(tmp_path: Path) -> None:
    step_file = tmp_path / "steps.yaml"
    step_file.write_text(
        """
workflowVersion: 1.0.0
jobs:
  - jobId: job-legacy
    steps:
      - stepId: \"1\"
        partId: PART_X
        displayName: Place part X
        sourcePrimPath: /Assembly/PART_X
        animationName: Insert_PART_X
        anchorType: ImageTarget
        targetId: MarkerX
        targetVersion: \"2026-03-10.1\"
        assetVersion: sha256_part_x
        instructionsShort: Place part X.
        expectedDurationSec: 10
""".strip(),
        encoding="utf-8",
    )

    repo = StepDefinitionRepository(step_definition_file=step_file)
    steps = repo.get_steps("job-legacy")

    assert len(steps) == 1
    parsed = steps[0]
    assert parsed.sequence_index == 1
    assert parsed.animation_start_step == 0
    assert parsed.animation_end_step == 0
    assert parsed.keep_visible_until_step == 0
    assert parsed.animation_layer_role == "animation"
    assert parsed.target_layer_role == "target-position"
    assert parsed.start_offset_xyz == (0.0, 0.1, 0.0)
    assert parsed.target_position_xyz == (0.0, 0.0, 0.0)
