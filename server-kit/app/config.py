from dataclasses import dataclass
import os
from pathlib import Path


@dataclass(frozen=True)
class AppConfig:
    http_host: str = "0.0.0.0"
    http_port: int = 8080
    grpc_host: str = "0.0.0.0"
    grpc_port: int = 50051
    log_level: str = "INFO"
    sample_root: Path = Path("shared/samples")
    manifests_root: Path = Path("shared/samples/manifests")
    asset_root: Path = Path("shared/samples/assets")
    target_root: Path = Path("shared/samples/targets")
    export_asset_root: Path = Path("shared/samples/assets")
    export_manifest_root: Path = Path("shared/samples/manifests")
    export_job_store_file: Path = Path("server-kit/runtime/export-jobs.json")
    export_job_processing_mode: str = "inline"
    export_job_retention_seconds: int = 86400
    export_worker_poll_seconds: float = 1.0
    step_definition_file: Path = Path("shared/samples/step-definitions.yaml")
    draco_enabled: bool = False
    draco_encoder_command_template: str = ""
    draco_toolchain: str = "gltf-transform"


    @classmethod
    def from_env(cls) -> "AppConfig":
        return cls(
            http_host=os.getenv("GUIDANCE_HTTP_HOST", "0.0.0.0"),
            http_port=int(os.getenv("GUIDANCE_HTTP_PORT", "8080")),
            grpc_host=os.getenv("GUIDANCE_GRPC_HOST", "0.0.0.0"),
            grpc_port=int(os.getenv("GUIDANCE_GRPC_PORT", "50051")),
            log_level=os.getenv("GUIDANCE_LOG_LEVEL", "INFO").upper(),
            sample_root=Path(os.getenv("GUIDANCE_SAMPLE_ROOT", "shared/samples")),
            manifests_root=Path(os.getenv("GUIDANCE_MANIFEST_ROOT", "shared/samples/manifests")),
            asset_root=Path(os.getenv("GUIDANCE_ASSET_ROOT", "shared/samples/assets")),
            target_root=Path(os.getenv("GUIDANCE_TARGET_ROOT", "shared/samples/targets")),
            export_asset_root=Path(
                os.getenv("GUIDANCE_EXPORT_ASSET_ROOT", "shared/samples/assets")
            ),
            export_manifest_root=Path(
                os.getenv("GUIDANCE_EXPORT_MANIFEST_ROOT", "shared/samples/manifests")
            ),
            export_job_store_file=Path(
                os.getenv("GUIDANCE_EXPORT_JOB_STORE_FILE", "server-kit/runtime/export-jobs.json")
            ),
            export_job_processing_mode=os.getenv("GUIDANCE_EXPORT_JOB_PROCESSING_MODE", "inline").lower(),
            export_job_retention_seconds=int(
                os.getenv("GUIDANCE_EXPORT_JOB_RETENTION_SECONDS", "86400")
            ),
            export_worker_poll_seconds=float(
                os.getenv("GUIDANCE_EXPORT_WORKER_POLL_SECONDS", "1.0")
            ),
            step_definition_file=Path(
                os.getenv("GUIDANCE_STEP_DEFINITION_FILE", "shared/samples/step-definitions.yaml")
            ),
            draco_enabled=os.getenv("GUIDANCE_DRACO_ENABLED", "false").lower() == "true",
            draco_encoder_command_template=os.getenv("GUIDANCE_DRACO_ENCODER_CMD", ""),
            draco_toolchain=os.getenv("GUIDANCE_DRACO_TOOLCHAIN", "gltf-transform"),
        )
