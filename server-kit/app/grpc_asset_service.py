"""gRPC asset streaming service with optional per-request Draco encoding."""

from collections.abc import Iterator
from pathlib import Path
from tempfile import TemporaryDirectory

import grpc

try:
    from .draco_codec import DracoCodec
    from .generated import guidance_pb2
    from .generated import guidance_pb2_grpc
    from .manifest_service import ManifestService
    from .logging_config import ContextAdapter
except ImportError:
    from draco_codec import DracoCodec
    from generated import guidance_pb2
    from generated import guidance_pb2_grpc
    from manifest_service import ManifestService
    from logging_config import ContextAdapter


class AssetTransferService(guidance_pb2_grpc.AssetTransferServiceServicer):
    """Streams step GLB or Vuforia target payloads in chunks to runtime clients."""

    def __init__(
        self,
        manifest_service: ManifestService,
        asset_root: Path,
        logger: ContextAdapter,
        draco_codec: DracoCodec,
        target_root: Path | None = None,
        chunk_size: int = 64 * 1024,
    ) -> None:
        self._manifest_service = manifest_service
        self._asset_root = asset_root
        self._target_root = target_root or asset_root
        self._logger = logger
        self._draco_codec = draco_codec
        self._chunk_size = chunk_size

    def StreamStepAsset(
        self,
        request: guidance_pb2.StepAssetStreamRequest,
        context: grpc.ServicerContext,
    ) -> Iterator[guidance_pb2.StepAssetChunk]:
        manifest = self._manifest_service.get_manifest(request.job_id)
        step = next((item for item in manifest.steps if item.step_id == request.step_id), None)
        if step is None:
            context.abort(grpc.StatusCode.NOT_FOUND, "Step not found in manifest")

        want_target = request.asset_type == guidance_pb2.ASSET_TYPE_VUFORIA_TARGET

        if want_target:
            file_name = step.target_file
            file_path = self._target_root / step.target_version / file_name
            if not file_path.exists():
                context.abort(grpc.StatusCode.NOT_FOUND, "Vuforia target file not found")
            yield from self._stream_file(
                request,
                step.target_version,
                file_name,
                file_path,
                guidance_pb2.ASSET_COMPRESSION_NONE,
            )
            return

        file_name = step.glb_file
        file_path = self._asset_root / step.asset_version / file_name
        if not file_path.exists():
            context.abort(grpc.StatusCode.NOT_FOUND, "GLB asset file not found")

        stream_path = file_path
        applied_compression = guidance_pb2.ASSET_COMPRESSION_NONE

        if request.preferred_compression == guidance_pb2.ASSET_COMPRESSION_DRACO:
            with TemporaryDirectory(prefix="guidance-draco-") as temp_dir:
                encoded_path = Path(temp_dir) / file_name
                if self._draco_codec.encode_to_draco(file_path, encoded_path):
                    stream_path = encoded_path
                    applied_compression = guidance_pb2.ASSET_COMPRESSION_DRACO
                    yield from self._stream_file(
                        request,
                        step.asset_version,
                        file_name,
                        stream_path,
                        applied_compression,
                    )
                    return

        yield from self._stream_file(
            request,
            step.asset_version,
            file_name,
            stream_path,
            applied_compression,
        )

    def _stream_file(
        self,
        request: guidance_pb2.StepAssetStreamRequest,
        asset_version: str,
        file_name: str,
        stream_path: Path,
        applied_compression: int,
    ) -> Iterator[guidance_pb2.StepAssetChunk]:

        self._logger.info(
            "stream step asset",
            session_id="-",
            step_id=request.step_id,
            event="grpc.asset.stream",
            correlation_id=asset_version,
        )

        with stream_path.open("rb") as handle:
            chunk_index = 0
            while True:
                data = handle.read(self._chunk_size)
                if not data:
                    break
                next_pos = handle.tell()
                total_size = stream_path.stat().st_size
                is_last = next_pos >= total_size

                chunk = guidance_pb2.StepAssetChunk(
                    job_id=request.job_id,
                    step_id=request.step_id,
                    asset_version=asset_version,
                    file_name=file_name,
                    applied_compression=applied_compression,
                    chunk_index=chunk_index,
                    data=data,
                    is_last=is_last,
                )
                yield chunk
                chunk_index += 1
