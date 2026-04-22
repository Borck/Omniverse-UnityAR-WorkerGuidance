from dataclasses import dataclass
import hashlib
import json

from app.guidance.repository import StepDefinition, StepDefinitionRepository


@dataclass(frozen=True)
class LayerPair:
    sequence_index: int
    animation_layer_id: str
    target_layer_id: str
    animation_layer_index: int
    target_layer_index: int


@dataclass(frozen=True)
class ResolvedStep:
    job_id: str
    step_id: str
    sequence_index: int
    part_id: str
    display_name: str
    source_prim_path: str
    active_prim_path: str
    animation_name: str
    animation_layer_role: str
    target_layer_role: str
    animation_layer_id: str
    target_layer_id: str
    animation_layer_index: int
    target_layer_index: int
    timeline_start_step: int
    timeline_end_step: int
    timeline_fps: int
    animation_start_step: int
    animation_end_step: int
    keep_visible_until_step: int
    handover_target_layer_id: str
    handover_next_animation_layer_id: str
    visible_layer_ids: tuple[str, ...]
    muted_layer_ids: tuple[str, ...]
    cache_key: str


class LayerStackResolver:
    """Resolves step/layer states based on BTU switch-step convention.

    Convention from btu.switch_step_layers_ui_copy:
    - Even indices (0,2,4,...) are animation layers.
    - Odd indices (1,3,5,...) are end-position (target) layers.
    - On completed step N: mute all; unmute target layers for 1..N; unmute animation for N+1 (if present).
    """

    def __init__(self, step_repository: StepDefinitionRepository) -> None:
        self._step_repository = step_repository

    @staticmethod
    def analyze_layer_pairs(sublayer_paths_bottom_to_top: list[str]) -> list[LayerPair]:
        pairs: list[LayerPair] = []
        for i in range(0, len(sublayer_paths_bottom_to_top), 2):
            animation_layer_id = sublayer_paths_bottom_to_top[i]
            target_layer_index = i + 1
            target_layer_id = (
                sublayer_paths_bottom_to_top[target_layer_index]
                if target_layer_index < len(sublayer_paths_bottom_to_top)
                else ""
            )
            pairs.append(
                LayerPair(
                    sequence_index=(i // 2) + 1,
                    animation_layer_id=animation_layer_id,
                    target_layer_id=target_layer_id,
                    animation_layer_index=i,
                    target_layer_index=target_layer_index,
                )
            )
        return pairs

    def resolve_steps(self, job_id: str, sublayer_paths_bottom_to_top: list[str]) -> list[ResolvedStep]:
        steps = self._sorted_steps(job_id)
        pairs = self.analyze_layer_pairs(sublayer_paths_bottom_to_top)

        resolved: list[ResolvedStep] = []
        for step in steps:
            pair = self._pair_for_sequence(pairs, step.sequence_index)
            next_pair = self._pair_for_sequence(pairs, step.sequence_index + 1)
            visible_layer_ids = self._visible_layers_for_completed_step(
                sequence_index=step.sequence_index,
                pairs=pairs,
            )
            muted_layer_ids = tuple(
                layer
                for layer in sublayer_paths_bottom_to_top
                if layer not in visible_layer_ids
            )
            cache_key = self._compute_cache_key(job_id, step, pair, visible_layer_ids)

            resolved.append(
                ResolvedStep(
                    job_id=job_id,
                    step_id=step.step_id,
                    sequence_index=step.sequence_index,
                    part_id=step.part_id,
                    display_name=step.display_name,
                    source_prim_path=step.source_prim_path,
                    active_prim_path=step.active_prim_path,
                    animation_name=step.animation_name,
                    animation_layer_role=step.animation_layer_role,
                    target_layer_role=step.target_layer_role,
                    animation_layer_id=pair.animation_layer_id,
                    target_layer_id=pair.target_layer_id,
                    animation_layer_index=pair.animation_layer_index,
                    target_layer_index=pair.target_layer_index,
                    timeline_start_step=step.timeline_start_step,
                    timeline_end_step=step.timeline_end_step,
                    timeline_fps=step.timeline_fps,
                    animation_start_step=step.animation_start_step,
                    animation_end_step=step.animation_end_step,
                    keep_visible_until_step=step.keep_visible_until_step,
                    handover_target_layer_id=pair.target_layer_id,
                    handover_next_animation_layer_id=next_pair.animation_layer_id,
                    visible_layer_ids=visible_layer_ids,
                    muted_layer_ids=muted_layer_ids,
                    cache_key=cache_key,
                )
            )
        return resolved

    def _sorted_steps(self, job_id: str) -> list[StepDefinition]:
        steps = self._step_repository.get_steps(job_id)
        return sorted(
            steps,
            key=lambda s: (
                s.sequence_index,
                int(s.step_id) if s.step_id.isdigit() else 0,
                s.step_id,
            ),
        )

    @staticmethod
    def _pair_for_sequence(pairs: list[LayerPair], sequence_index: int) -> LayerPair:
        for pair in pairs:
            if pair.sequence_index == sequence_index:
                return pair
        return LayerPair(
            sequence_index=sequence_index,
            animation_layer_id="",
            target_layer_id="",
            animation_layer_index=-1,
            target_layer_index=-1,
        )

    @staticmethod
    def _visible_layers_for_completed_step(sequence_index: int, pairs: list[LayerPair]) -> tuple[str, ...]:
        visible: list[str] = []

        # BTU behavior: unmute end-position layers for all completed steps 1..N
        for pair in pairs:
            if pair.sequence_index <= sequence_index and pair.target_layer_id:
                visible.append(pair.target_layer_id)

        # BTU behavior: unmute next animation layer (N+1), if available
        next_pair = next((p for p in pairs if p.sequence_index == sequence_index + 1), None)
        if next_pair is not None and next_pair.animation_layer_id:
            visible.append(next_pair.animation_layer_id)

        return tuple(visible)

    @staticmethod
    def _compute_cache_key(
        job_id: str,
        step: StepDefinition,
        pair: LayerPair,
        visible_layer_ids: tuple[str, ...],
    ) -> str:
        payload = {
            "jobId": job_id,
            "stepId": step.step_id,
            "sequenceIndex": step.sequence_index,
            "partId": step.part_id,
            "sourcePrimPath": step.source_prim_path,
            "animationName": step.animation_name,
            "animationWindow": [step.animation_start_step, step.animation_end_step],
            "keepVisibleUntil": step.keep_visible_until_step,
            "pair": {
                "animationLayerId": pair.animation_layer_id,
                "targetLayerId": pair.target_layer_id,
                "animationLayerIndex": pair.animation_layer_index,
                "targetLayerIndex": pair.target_layer_index,
            },
            "visibleLayerIds": list(visible_layer_ids),
        }
        raw = json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
        return hashlib.sha256(raw).hexdigest()
