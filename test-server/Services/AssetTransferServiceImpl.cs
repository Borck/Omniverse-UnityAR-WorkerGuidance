using Grpc.Core;
using Guidance.V1;
using GuidanceAdminServer.Storage;

namespace GuidanceAdminServer.Services;

/// <summary>
/// gRPC implementation of <see cref="AssetTransferService"/>.
/// Streams GLB model files or Vuforia target files in 64 KB chunks.
/// </summary>
public sealed class AssetTransferServiceImpl : AssetTransferService.AssetTransferServiceBase
{
    private const int ChunkSize = 64 * 1024;

    private readonly JobStore _jobStore;
    private readonly FileAssetStore _assetStore;
    private readonly ILogger<AssetTransferServiceImpl> _logger;

    public AssetTransferServiceImpl(
        JobStore jobStore,
        FileAssetStore assetStore,
        ILogger<AssetTransferServiceImpl> logger)
    {
        _jobStore = jobStore;
        _assetStore = assetStore;
        _logger = logger;
    }

    public override async Task StreamStepAsset(
        StepAssetStreamRequest request,
        IServerStreamWriter<StepAssetChunk> responseStream,
        ServerCallContext context)
    {
        var job = _jobStore.Get(request.JobId);
        if (job is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Job '{request.JobId}' not found"));
        }

        var step = job.Steps.FirstOrDefault(s => s.StepId == request.StepId);
        if (step is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Step '{request.StepId}' not found in job '{request.JobId}'"));
        }

        bool wantTarget = request.AssetType == AssetType.VuforiaTarget;

        string filePath;
        string assetVersion;
        string fileName;

        if (wantTarget)
        {
            fileName = step.TargetFileName;
            assetVersion = step.TargetVersion;
            filePath = _assetStore.GetTargetPath(assetVersion, fileName);
        }
        else
        {
            fileName = step.GlbFileName;
            assetVersion = step.AssetVersion;
            filePath = _assetStore.GetGlbPath(assetVersion, fileName);
        }

        if (!File.Exists(filePath))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Asset file not found: {filePath}"));
        }

        _logger.LogInformation(
            "Streaming {Type} job={Job} step={Step} file={File}",
            wantTarget ? "target" : "glb", request.JobId, request.StepId, fileName);

        await using var fileStream = File.OpenRead(filePath);
        var buffer = new byte[ChunkSize];
        int chunkIndex = 0;
        int bytesRead;
        long totalSize = fileStream.Length;

        while ((bytesRead = await fileStream.ReadAsync(buffer, context.CancellationToken)) > 0)
        {
            var isLast = fileStream.Position >= totalSize;
            await responseStream.WriteAsync(new StepAssetChunk
            {
                JobId = request.JobId,
                StepId = request.StepId,
                AssetVersion = assetVersion,
                FileName = fileName,
                AppliedCompression = AssetCompression.None,
                ChunkIndex = chunkIndex++,
                Data = Google.Protobuf.ByteString.CopyFrom(buffer, 0, bytesRead),
                IsLast = isLast,
            });
        }
    }
}
