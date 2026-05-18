from dataclasses import dataclass
import os
from pathlib import Path

#  ─── Omniverse Connection Config ────────────────────────────────────────────────
SERVER = "omniverse://141.43.76.21"
REPO_ROOT = Path(__file__).resolve().parents[3]  # → Omniverse-UnityAR-WorkerGuidance/

@dataclass(frozen=True)
class AppConfig:
    """Holds all runtime configuration used by HTTP/gRPC and packaging services."""
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
    session_store_file: Path = Path("server-kit/runtime/sessions.json")
    step_definition_file: Path = Path("shared/samples/step-definitions.yaml")
    draco_enabled: bool = False
    draco_encoder_command_template: str = ""
    draco_toolchain: str = "gltf-transform"
    stage_uri: str = ""

    @classmethod
    def from_env(cls) -> "AppConfig":
        """Builds configuration from process environment variables."""
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
                os.getenv("GUIDANCE_EXPORT_JOB_STORE", "server-kit/runtime/export-jobs.json")
            ),
            export_job_processing_mode=os.getenv("GUIDANCE_EXPORT_MODE", "inline"),
            export_job_retention_seconds=int(os.getenv("GUIDANCE_EXPORT_RETENTION", "86400")),
            export_worker_poll_seconds=float(os.getenv("GUIDANCE_EXPORT_POLL", "1.0")),
            session_store_file=Path(
                os.getenv("GUIDANCE_SESSION_STORE", "server-kit/runtime/sessions.json")
            ),
            step_definition_file=Path(
                os.getenv("GUIDANCE_STEP_DEFS", "shared/samples/step-definitions.yaml")
            ),
            draco_enabled=os.getenv("GUIDANCE_DRACO_ENABLED", "false").lower() == "true",
            draco_encoder_command_template=os.getenv("GUIDANCE_DRACO_CMD", ""),
            draco_toolchain=os.getenv("GUIDANCE_DRACO_TOOLCHAIN", "gltf-transform"),
            stage_uri=os.getenv("GUIDANCE_STAGE_URI", ""),
        )
