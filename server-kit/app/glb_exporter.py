"""GLB export backends and Omniverse stage-export adapter.

The export pipeline only requires a synchronous `export_glb(source, output)` backend.
This module additionally exposes an Omniverse-oriented async stage export path modeled
after the validated reference implementation (flatten composed stage + convert to GLB).
"""

from __future__ import annotations

import asyncio
import importlib
import logging
import os
import posixpath
from pathlib import Path
from shutil import copyfile
import tempfile
from typing import Any, Callable, Protocol


logger = logging.getLogger(__name__)


class GlbExportBackend(Protocol):
    """Contract for converting or copying source input into a runtime GLB output."""

    def export_glb(self, source_glb: Path, output_glb: Path) -> bool:
        """Exports source payload to output path and returns success state."""
        ...


class PassthroughGlbExporter:
    """Default backend that performs a direct file copy without Omniverse APIs."""

    def export_glb(self, source_glb: Path, output_glb: Path) -> bool:
        output_glb.parent.mkdir(parents=True, exist_ok=True)
        copyfile(source_glb, output_glb)
        return True


class OmniverseStageGlbExporter:
    """Omniverse-adapted stage exporter with tested converter settings.

    Notes:
    - `export_glb(...)` remains sync and pipeline-compatible (copy fallback).
    - `export_current_stage_to_glb_async(...)` is the Omniverse stage path.
    - Omniverse modules are loaded lazily to keep non-Omniverse environments working.
    """

    CONVERTER_SETTINGS: dict[str, Any] = {
        "bake_mdl_material": False,
        "baking_scales": False,
        "convert_fbx_to_y_up": False,
        "convert_fbx_to_z_up": False,
        "create_world_as_default_root_prim": True,
        "disabling_instancing": False,
        "embed_mdl_in_usd": True,
        "embed_textures": False,
        "export_hidden_props": False,
        "export_mdl_gltf_extension": False,
        "export_preview_surface": False,
        "export_separate_gltf": False,
        "ignore_animations": False,
        "ignore_camera": False,
        "ignore_flip_rotations": False,
        "ignore_light": False,
        "ignore_materials": True,
        "ignore_pivots": False,
        "ignore_unbound_bones": False,
        "keep_all_materials": False,
        "merge_all_meshes": False,
        "single_mesh": False,
        "smooth_normals": True,
        "support_point_instancer": False,
        "use_double_precision_to_usd_transform_op": False,
        "use_meter_as_world_unit": True,
    }

    def __init__(
        self,
        progress_callback: Callable[[int, int], None] | None = None,
    ) -> None:
        self._external_progress_callback = progress_callback

    @staticmethod
    def derive_default_output_path(stage_url: str, step_number: int) -> str:
        """Builds GLB output path from stage URL, compatible with omniverse:// paths."""
        normalized = (stage_url or "").strip()
        if normalized and not normalized.startswith("anon:"):
            path_mod = posixpath if "://" in normalized else os.path
            base_name = path_mod.splitext(path_mod.basename(normalized))[0]
            dir_name = path_mod.dirname(normalized)
            return f"{dir_name}/Export_GLB/{base_name}_step{step_number}.glb"

        return os.path.join(
            os.path.expanduser("~"),
            "Export_GLB",
            f"exported_stage_step{step_number}.glb",
        )

    def export_glb(self, source_glb: Path, output_glb: Path) -> bool:
        """Sync fallback for pipeline usage: copies source GLB to output path."""
        try:
            output_glb.parent.mkdir(parents=True, exist_ok=True)
            copyfile(source_glb, output_glb)
            return True
        except OSError:
            return False

    async def wait_for_stage_ready(self, timeout: float = 10.0) -> bool:
        """Waits until omniverse stage context is open and ready."""
        omni_usd, _ = self._load_omni_modules()
        if omni_usd is None:
            return False

        usd_context = omni_usd.get_context()
        elapsed = 0.0
        interval = 0.1

        while elapsed < timeout:
            stage = usd_context.get_stage()
            if stage and usd_context.get_stage_state() == omni_usd.StageState.OPENED:
                return True
            await asyncio.sleep(interval)
            elapsed += interval

        logger.error("Timeout waiting for USD stage to be ready")
        return False

    async def export_current_stage_to_glb_async(self, output_path: str) -> bool:
        """Exports current composed stage to GLB using flatten+convert flow.

        Flow mirrors the validated reference implementation:
        1) flatten composed stage via `export_as_stage_async` (respects muted layers),
        2) convert flattened USD to GLB via `omni.kit.asset_converter`.
        """
        omni_usd, asset_converter = self._load_omni_modules()
        if omni_usd is None or asset_converter is None:
            logger.error("Omniverse modules are unavailable; cannot export current stage")
            return False

        usd_context = omni_usd.get_context()
        stage = usd_context.get_stage()
        if not stage:
            logger.error("No USD stage available for export")
            return False

        temp_dir = tempfile.mkdtemp(prefix="guidance-stage-export-")
        temp_usd_path = Path(temp_dir) / "flattened_stage.usd"

        try:
            export_result = await usd_context.export_as_stage_async(str(temp_usd_path))
            if isinstance(export_result, tuple):
                result, error_msg = export_result
            else:
                result = bool(export_result)
                error_msg = "Unknown error"

            if not result:
                logger.error("Failed to flatten stage: %s", error_msg)
                return False

            if "://" not in output_path:
                output_dir = Path(output_path).parent
                if str(output_dir):
                    output_dir.mkdir(parents=True, exist_ok=True)

            converter_context = asset_converter.AssetConverterContext()
            self._apply_converter_settings(converter_context)

            converter = asset_converter.get_instance()
            task = converter.create_converter_task(
                str(temp_usd_path),
                output_path,
                self._progress_callback,
                converter_context,
            )

            success = await task.wait_until_finished()
            if success:
                logger.info("GLB export successful: %s", output_path)
            else:
                logger.error(
                    "GLB export failed: %s (status: %s)",
                    task.get_error_message(),
                    task.get_status(),
                )
            return bool(success)
        except Exception:
            logger.exception("GLB export error")
            return False
        finally:
            try:
                for entry in Path(temp_dir).glob("**/*"):
                    if entry.is_file():
                        entry.unlink(missing_ok=True)
                Path(temp_dir).rmdir()
            except OSError:
                logger.warning("Could not clean up temp files in %s", temp_dir)

    def _apply_converter_settings(self, context: Any) -> None:
        """Applies known converter settings to AssetConverterContext."""
        for key, value in self.CONVERTER_SETTINGS.items():
            if hasattr(context, key):
                setattr(context, key, value)
            else:
                logger.warning("AssetConverterContext has no attribute '%s', skipping", key)

    def _progress_callback(self, current_step: int, total: int) -> None:
        """Logs converter progress and forwards updates to optional callback."""
        if total > 0:
            pct = (current_step / total) * 100.0
            logger.debug("GLB export progress: %s/%s (%.1f%%)", current_step, total, pct)
        else:
            logger.debug("GLB export processing step %s", current_step)

        if self._external_progress_callback is not None:
            self._external_progress_callback(current_step, total)

    @staticmethod
    def _load_omni_modules() -> tuple[Any | None, Any | None]:
        """Loads Omniverse modules lazily to avoid hard dependency at import time."""
        try:
            omni_usd = importlib.import_module("omni.usd")
            asset_converter = importlib.import_module("omni.kit.asset_converter")
            return omni_usd, asset_converter
        except Exception:
            return None, None
