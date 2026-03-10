from pathlib import Path
import sys

sys.path.append(str(Path(__file__).resolve().parents[1]))

from app.layer_stack_resolver import LayerStackResolver
from app.step_definition_repository import StepDefinitionRepository


def _resolver() -> LayerStackResolver:
    repo = StepDefinitionRepository(step_definition_file=Path("shared/samples/step-definitions.yaml"))
    return LayerStackResolver(step_repository=repo)


def test_analyze_layer_pairs_matches_btu_even_odd_convention() -> None:
    resolver = _resolver()
    sublayers = [
        "PLATE_BOTTOM_01_001.usd",
        "PLATE_BOTTOM_01_001-Position.usd",
        "CORES_001_CORES_002.usd",
        "CORES_001_CORES_002-Position.usd",
        "FRAME_01.usd",
        "FRAME_01-Position.usd",
    ]

    pairs = resolver.analyze_layer_pairs(sublayers)

    assert len(pairs) == 3
    assert pairs[0].sequence_index == 1
    assert pairs[0].animation_layer_id == "PLATE_BOTTOM_01_001.usd"
    assert pairs[0].target_layer_id == "PLATE_BOTTOM_01_001-Position.usd"
    assert pairs[0].animation_layer_index == 0
    assert pairs[0].target_layer_index == 1

    assert pairs[1].sequence_index == 2
    assert pairs[1].animation_layer_id == "CORES_001_CORES_002.usd"
    assert pairs[1].target_layer_id == "CORES_001_CORES_002-Position.usd"


def test_resolve_steps_uses_btu_visibility_rule() -> None:
    resolver = _resolver()
    sublayers = [
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

    resolved = resolver.resolve_steps("job-layer-example-001", sublayers)

    assert len(resolved) == 5

    # Step 1 complete -> show PART_A end-position + PART_B animation
    assert resolved[0].active_prim_path == "/Assembly/PART_A"
    assert resolved[0].timeline_start_step == 1
    assert resolved[0].timeline_end_step == 101
    assert resolved[0].timeline_fps == 30
    assert resolved[0].animation_layer_role == "animation"
    assert resolved[0].target_layer_role == "target-position"
    assert resolved[0].handover_target_layer_id == "PART_A-Position.usd"
    assert resolved[0].handover_next_animation_layer_id == "PART_B_anim.usd"
    assert resolved[0].visible_layer_ids == (
        "PART_A-Position.usd",
        "PART_B_anim.usd",
    )

    # Step 3 complete -> show end-position for A/B/C + PART_D animation
    assert resolved[2].visible_layer_ids == (
        "PART_A-Position.usd",
        "PART_B-Position.usd",
        "PART_C-Position.usd",
        "PART_D_anim.usd",
    )

    # Final step complete -> show all end-position layers, no next animation
    assert resolved[4].handover_target_layer_id == "PART_E-Position.usd"
    assert resolved[4].handover_next_animation_layer_id == ""
    assert resolved[4].visible_layer_ids == (
        "PART_A-Position.usd",
        "PART_B-Position.usd",
        "PART_C-Position.usd",
        "PART_D-Position.usd",
        "PART_E-Position.usd",
    )


def test_resolve_steps_produces_stable_cache_key_and_changes_when_layer_changes() -> None:
    resolver = _resolver()
    sublayers = [
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

    first = resolver.resolve_steps("job-layer-example-001", sublayers)
    second = resolver.resolve_steps("job-layer-example-001", sublayers)

    assert first[0].cache_key == second[0].cache_key

    changed_layers = sublayers.copy()
    changed_layers[1] = "PART_A-Position-v2.usd"
    changed = resolver.resolve_steps("job-layer-example-001", changed_layers)

    assert first[0].cache_key != changed[0].cache_key
