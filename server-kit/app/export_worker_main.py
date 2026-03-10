from pathlib import Path
import time

try:
    from .config import AppConfig
    from .draco_codec import DracoCodec
    from .draco_codec import DracoCodecConfig
    from .export_job_service import ExportJobService
    from .export_pipeline import StepPackageExporter
    from .logging_config import configure_logging
    from .manifest_service import ManifestService
except ImportError:
    from config import AppConfig
    from draco_codec import DracoCodec
    from draco_codec import DracoCodecConfig
    from export_job_service import ExportJobService
    from export_pipeline import StepPackageExporter
    from logging_config import configure_logging
    from manifest_service import ManifestService


def run_export_worker(config: AppConfig) -> None:
    repo_root = Path(__file__).resolve().parents[2]
    logger = configure_logging(config.log_level)

    manifest_service = ManifestService(manifests_root=repo_root / config.manifests_root)
    draco_codec = DracoCodec(
        DracoCodecConfig(
            enabled=config.draco_enabled,
            encoder_command_template=config.draco_encoder_command_template,
            toolchain=config.draco_toolchain,
        )
    )
    exporter = StepPackageExporter(
        manifest_service=manifest_service,
        source_asset_root=repo_root / config.asset_root,
        output_asset_root=repo_root / config.export_asset_root,
        output_manifest_root=repo_root / config.export_manifest_root,
        draco_codec=draco_codec,
    )
    job_service = ExportJobService(
        exporter=exporter,
        store_file=repo_root / config.export_job_store_file,
    )

    logger.info("export worker started", session_id="-", step_id="-", event="worker.start")

    while True:
        job_service.reload_from_store()
        run_id = job_service.process_next_queued()
        if run_id:
            logger.info(
                "export job processed",
                session_id="-",
                step_id="-",
                event="worker.process",
                correlation_id=run_id,
            )
            continue
        time.sleep(max(config.export_worker_poll_seconds, 0.1))


def main() -> None:
    run_export_worker(AppConfig.from_env())


if __name__ == "__main__":
    main()
