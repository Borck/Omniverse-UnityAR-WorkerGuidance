from dataclasses import dataclass
import os
from pathlib import Path

#  ─── Omniverse Connection Config ────────────────────────────────────────────────
REPO_ROOT = Path(__file__).resolve().parents[3]  # → Omniverse-UnityAR-WorkerGuidance/


def load_dotenv_file(path: "Path | None" = None) -> None:
    """Load KEY=VALUE pairs from the repo-root .env into os.environ.

    Dependency-free (no python-dotenv). Real environment variables always win:
    a key already present in os.environ is never overwritten, so container/CI
    config takes precedence over the committed .env. Quotes around values are
    stripped; blank lines and `#` comments are ignored.
    """
    env_path = path or (REPO_ROOT / ".env")
    if not env_path.is_file():
        return
    for raw in env_path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        key = key.strip()
        if not key or key in os.environ:
            continue
        value = value.strip().strip('"').strip("'")
        os.environ[key] = value


# Load .env once at import so every os.getenv below (and AppConfig.from_env)
# sees the file-backed values.
load_dotenv_file()

# Keys of the Nucleus servers we read from the environment, in display order.
NUCLEUS_KEYS = ("a", "b")

# Neutral fallback used only when no OMNI_*_SERVER (and no OMNI_SERVER) is
# configured in the environment. Real hosts must be supplied via .env /
# environment variables — never hardcode a real Nucleus host here.
_LEGACY_DEFAULT_SERVER = "omniverse://localhost"


@dataclass(frozen=True)
class NucleusEndpoint:
    """One Omniverse Nucleus server plus the credentials used to reach it."""
    key: str
    name: str
    server: str
    user: str
    password: str


def _load_endpoint(key: str) -> "NucleusEndpoint | None":
    """Read one OMNI_<KEY>_* endpoint from the environment, or None if unset."""
    prefix = f"OMNI_{key.upper()}_"
    server = os.getenv(prefix + "SERVER", "").strip()
    if not server:
        return None
    return NucleusEndpoint(
        key=key.lower(),
        name=os.getenv(prefix + "NAME", key.upper()).strip() or key.upper(),
        server=server.rstrip("/"),
        user=os.getenv(prefix + "USER", "").strip(),
        password=os.getenv(prefix + "PASS", ""),
    )


def load_nucleus_endpoints() -> "dict[str, NucleusEndpoint]":
    """Build the ordered map of configured Nucleus endpoints from the env.

    Falls back to a single legacy endpoint (the old hardcoded host, with
    OMNI_USER/OMNI_PASS if present) when nothing is configured, so existing
    deployments keep working without a .env.
    """
    endpoints: dict[str, NucleusEndpoint] = {}
    for key in NUCLEUS_KEYS:
        ep = _load_endpoint(key)
        if ep is not None:
            endpoints[ep.key] = ep
    if not endpoints:
        endpoints["a"] = NucleusEndpoint(
            key="a",
            name="Default",
            server=os.getenv("OMNI_SERVER", _LEGACY_DEFAULT_SERVER).rstrip("/"),
            user=os.getenv("OMNI_USER", ""),
            password=os.getenv("OMNI_PASS", ""),
        )
    return endpoints


def default_active_key(endpoints: "dict[str, NucleusEndpoint]") -> str:
    """Resolve the boot-time active key from OMNI_ACTIVE_NUCLEUS, with fallback."""
    key = os.getenv("OMNI_ACTIVE_NUCLEUS", "").strip().lower()
    if key in endpoints:
        return key
    return next(iter(endpoints))

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
