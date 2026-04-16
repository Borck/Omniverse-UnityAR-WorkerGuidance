"""Combined gRPC server bootstrap for session and asset transfer services."""

try:
    from .config import AppConfig
    from .draco_codec import DracoCodec
    from .draco_codec import DracoCodecConfig
    from .grpc_asset_service import AssetTransferService
    from .manifest_service import ManifestService
    from .generated import guidance_pb2_grpc
    from .grpc_session_service import GuidanceSessionService
    from .guidance_server import SessionManager
    from .logging_config import configure_logging
    from .step_definition_repository import StepDefinitionRepository
except ImportError:
    from config import AppConfig
    from draco_codec import DracoCodec
    from draco_codec import DracoCodecConfig
    from grpc_asset_service import AssetTransferService
    from manifest_service import ManifestService
    from generated import guidance_pb2_grpc
    from grpc_session_service import GuidanceSessionService
    from guidance_server import SessionManager
    from logging_config import configure_logging
    from step_definition_repository import StepDefinitionRepository
from pathlib import Path
import grpc
from concurrent import futures


def run_combined_grpc_server(config: AppConfig) -> None:
    logger = configure_logging(config.log_level)
    repo_root = Path(__file__).resolve().parents[2]
    manifest_service = ManifestService(manifests_root=repo_root / config.manifests_root)
    asset_root = repo_root / config.asset_root
    target_root = repo_root / config.target_root
    draco_codec = DracoCodec(
        DracoCodecConfig(
            enabled=config.draco_enabled,
            encoder_command_template=config.draco_encoder_command_template,
            toolchain=config.draco_toolchain,
        )
    )

    server = grpc.server(futures.ThreadPoolExecutor(max_workers=8))
    step_repository = StepDefinitionRepository(
        step_definition_file=repo_root / config.step_definition_file,
    )
    guidance_pb2_grpc.add_GuidanceSessionServiceServicer_to_server(
        GuidanceSessionService(
            session_manager=SessionManager(store_file=repo_root / config.session_store_file),
            logger=logger,
            step_repository=step_repository,
            default_job_id="job-mock-001",
        ),
        server,
    )
    guidance_pb2_grpc.add_AssetTransferServiceServicer_to_server(
        AssetTransferService(
            manifest_service=manifest_service,
            asset_root=asset_root,
            target_root=target_root,
            logger=logger,
            draco_codec=draco_codec,
        ),
        server,
    )
    server.add_insecure_port(f"{config.grpc_host}:{config.grpc_port}")
    server.start()
    logger.info("grpc service started", session_id="-", step_id="-", event="grpc.start")
    server.wait_for_termination()


def main() -> None:
    config = AppConfig.from_env()
    run_combined_grpc_server(config)


if __name__ == "__main__":
    main()
