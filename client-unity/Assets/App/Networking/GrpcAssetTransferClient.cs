using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Guidance.V1;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Streams step assets (GLB or Vuforia model target) from the server via gRPC
    /// and writes the assembled payload to the local target payload cache.
    /// </summary>
    public sealed class GrpcAssetTransferClient
    {
        private readonly string _target;

        public GrpcAssetTransferClient(string target)
        {
            _target = target;
        }

        /// <summary>
        /// Downloads a Vuforia model target file for the given step via gRPC chunk streaming,
        /// saves it to <paramref name="outputPath"/>, then invokes <paramref name="onReady"/>.
        /// </summary>
        public IEnumerator StreamTargetAsync(
            string jobId,
            string stepId,
            string targetVersion,
            string outputPath,
            Action<string> onReady,
            Action<string> onError)
        {
            Task<bool> task = null;
            try
            {
                task = StreamTargetInternalAsync(jobId, stepId, targetVersion, outputPath, CancellationToken.None);
            }
            catch (Exception ex)
            {
                onError?.Invoke($"gRPC target stream setup failed: {ex.Message}");
                yield break;
            }

            yield return new WaitUntil(() => task.IsCompleted);

            if (task.IsFaulted)
            {
                onError?.Invoke($"gRPC target stream failed: {task.Exception?.GetBaseException().Message}");
                yield break;
            }

            if (!task.Result)
            {
                onError?.Invoke("gRPC target stream returned no data");
                yield break;
            }

            onReady?.Invoke(outputPath);
        }

        private async Task<bool> StreamTargetInternalAsync(
            string jobId,
            string stepId,
            string targetVersion,
            string outputPath,
            CancellationToken ct)
        {
            var channel = new Channel(_target, ChannelCredentials.Insecure);
            try
            {
                var client = new AssetTransferService.AssetTransferServiceClient(channel);
                var request = new StepAssetStreamRequest
                {
                    JobId = jobId,
                    StepId = stepId,
                    PreferredCompression = AssetCompression.None,
                    AssetType = AssetType.VuforiaTarget,
                };

                var assembler = new AssetStreamAssembler(supportsDraco: false);
                byte[] payload = null;

                using var call = client.StreamStepAsset(request, cancellationToken: ct);
                while (await call.ResponseStream.MoveNext(ct))
                {
                    var chunk = call.ResponseStream.Current;
                    var chunkDto = new AssetChunkDto
                    {
                        JobId = chunk.JobId,
                        StepId = chunk.StepId,
                        AssetVersion = chunk.AssetVersion,
                        FileName = chunk.FileName,
                        AppliedCompression = chunk.AppliedCompression == AssetCompression.Draco
                            ? AssetCompressionMode.Draco
                            : AssetCompressionMode.None,
                        ChunkIndex = chunk.ChunkIndex,
                        Data = chunk.Data.ToByteArray(),
                        IsLast = chunk.IsLast,
                    };
                    var assembled = assembler.AppendChunk(chunkDto);
                    if (chunkDto.IsLast && assembled.Length > 0)
                    {
                        payload = assembled;
                    }
                }

                if (payload == null || payload.Length == 0)
                {
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? string.Empty);
                assembler.SaveToFile(payload, outputPath);
                Debug.Log($"[GrpcAssetTransferClient] Vuforia target saved: {outputPath} ({payload.Length} bytes)");
                return true;
            }
            finally
            {
                await channel.ShutdownAsync();
            }
        }
    }
}
