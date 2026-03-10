from concurrent import futures
from pathlib import Path
import socket
import sys

import grpc

sys.path.append(str(Path(__file__).resolve().parents[1]))
sys.path.append(str(Path(__file__).resolve().parents[1] / "app" / "generated"))

from app.generated import guidance_pb2
from app.generated import guidance_pb2_grpc
from app.grpc_asset_service import AssetTransferService
from app.logging_config import configure_logging
from app.manifest_service import ManifestService


class FakeDracoCodec:
    def is_supported(self) -> bool:
        return True

    def encode_to_draco(self, source_glb: Path, target_glb: Path) -> bool:
        # Fake compression path for integration contract testing.
        target_glb.write_bytes(source_glb.read_bytes())
        return True


def _get_free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


def test_stream_step_asset_marks_draco_when_encoder_available() -> None:
    port = _get_free_port()
    repo_root = Path(__file__).resolve().parents[2]

    manifest_service = ManifestService(repo_root / "shared" / "samples" / "manifests")
    asset_root = repo_root / "shared" / "samples" / "assets"
    logger = configure_logging("INFO")

    server = grpc.server(futures.ThreadPoolExecutor(max_workers=4))
    guidance_pb2_grpc.add_AssetTransferServiceServicer_to_server(
        AssetTransferService(
            manifest_service=manifest_service,
            asset_root=asset_root,
            logger=logger,
            draco_codec=FakeDracoCodec(),
        ),
        server,
    )
    server.add_insecure_port(f"127.0.0.1:{port}")
    server.start()

    request = guidance_pb2.StepAssetStreamRequest(
        job_id="job-mock-001",
        step_id="17",
        preferred_compression=guidance_pb2.ASSET_COMPRESSION_DRACO,
    )

    with grpc.insecure_channel(f"127.0.0.1:{port}") as channel:
        stub = guidance_pb2_grpc.AssetTransferServiceStub(channel)
        first_chunk = next(stub.StreamStepAsset(request))

    assert first_chunk.applied_compression == guidance_pb2.ASSET_COMPRESSION_DRACO
    server.stop(grace=0)
