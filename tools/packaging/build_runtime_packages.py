import argparse
from pathlib import Path
import sys


REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.append(str(REPO_ROOT / "server-kit" / "app"))

from config import AppConfig
from draco_codec import DracoCodec, DracoCodecConfig
from export_pipeline import StepPackageExporter
from manifest_service import ManifestService


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build runtime-ready step packages and update immutable manifests."
    )
    parser.add_argument("--job-id", required=True, help="Job identifier to package")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    config = AppConfig.from_env()

    manifest_service = ManifestService(REPO_ROOT / config.manifests_root)
    draco_codec = DracoCodec(
        DracoCodecConfig(
            enabled=config.draco_enabled,
            encoder_command_template=config.draco_encoder_command_template,
            toolchain=config.draco_toolchain,
        )
    )

    exporter = StepPackageExporter(
        manifest_service=manifest_service,
        source_asset_root=REPO_ROOT / config.asset_root,
        output_asset_root=REPO_ROOT / config.export_asset_root,
        output_manifest_root=REPO_ROOT / config.export_manifest_root,
        draco_codec=draco_codec,
    )

    result = exporter.build_job_packages(job_id=args.job_id)
    print(f"Exported {result.generated_steps} step packages to {result.manifest_path}")


if __name__ == "__main__":
    main()
