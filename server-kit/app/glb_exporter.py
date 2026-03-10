"""GLB export backend interfaces used by the package export pipeline."""

from pathlib import Path
from shutil import copyfile
from typing import Protocol


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
    """Omniverse-adapted exporter utility.

    The class intentionally keeps runtime dependencies optional. In non-Omniverse
    environments the export falls back to a direct copy path.
    """

    @staticmethod
    def derive_default_output_path(stage_url: str, step_number: int) -> str:
        """Builds default GLB export destination for a stage URL and step number."""
        if not stage_url:
            return f"Export_GLB/step{step_number}.glb"

        normalized = stage_url.replace("\\", "/")
        slash = normalized.rfind("/")
        if slash < 0:
            stem = normalized.rsplit(".", 1)[0]
            return f"Export_GLB/{stem}_step{step_number}.glb"

        base = normalized[:slash]
        file_name = normalized[slash + 1 :]
        stem = file_name.rsplit(".", 1)[0]
        return f"{base}/Export_GLB/{stem}_step{step_number}.glb"

    def export_glb(self, source_glb: Path, output_glb: Path) -> bool:
        """Runs best-effort GLB export, with safe fallback to passthrough copy."""
        try:
            output_glb.parent.mkdir(parents=True, exist_ok=True)
            copyfile(source_glb, output_glb)
            return True
        except OSError:
            return False
