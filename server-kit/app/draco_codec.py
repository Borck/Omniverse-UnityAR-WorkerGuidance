from dataclasses import dataclass
from pathlib import Path
import shlex
import shutil
import subprocess


@dataclass(frozen=True)
class DracoCodecConfig:
    enabled: bool
    encoder_command_template: str = ""
    toolchain: str = "gltf-transform"


class DracoCodec:
    _TOOLCHAIN_COMMANDS: dict[str, str] = {
        "gltf-transform": (
            "gltf-transform draco \"{input}\" \"{output}\" "
            "--method edgebreaker --quantize-position 14"
        ),
        "draco-encoder": "draco_encoder -i \"{input}\" -o \"{output}\"",
    }

    def __init__(self, config: DracoCodecConfig) -> None:
        self._config = config

    def _resolve_command_template(self) -> str:
        template = self._config.encoder_command_template.strip()
        if template:
            return template
        return self._TOOLCHAIN_COMMANDS.get(self._config.toolchain.strip().lower(), "")

    def _resolve_executable(self) -> str:
        template = self._resolve_command_template()
        if not template:
            return ""
        try:
            return shlex.split(template, posix=False)[0]
        except ValueError:
            return template.split()[0]

    def is_supported(self) -> bool:
        if not self._config.enabled:
            return False
        executable = self._resolve_executable()
        if not executable:
            return False
        return shutil.which(executable) is not None

    def encode_to_draco(self, source_glb: Path, target_glb: Path) -> bool:
        if not self._config.enabled:
            return False

        command_template = self._resolve_command_template()
        if not command_template:
            return False

        executable = self._resolve_executable()
        if not executable or shutil.which(executable) is None:
            return False

        command = command_template.format(
            input=str(source_glb),
            output=str(target_glb),
        )

        # The encoder command is user-configured; this keeps the implementation
        # flexible across environments and tools.
        completed = subprocess.run(
            command,
            shell=True,
            check=False,
            capture_output=True,
            text=True,
        )
        return completed.returncode == 0 and target_glb.exists() and target_glb.stat().st_size > 0
