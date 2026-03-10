using UnityEngine;

namespace Guidance.Runtime
{
    public sealed class SessionClient
    {
        public bool SupportsDraco { get; }
        private readonly AssetStreamAssembler _assembler;

        public SessionClient(bool supportsDraco)
        {
            SupportsDraco = supportsDraco;
            _assembler = new AssetStreamAssembler(SupportsDraco);
        }

        public void Initialize()
        {
            var compressionMode = SupportsDraco ? "draco-enabled" : "draco-disabled";
            Debug.Log($"[SessionClient] Initialized ({compressionMode}, gRPC stream not wired yet).");
        }

        public void HandleAssetChunk(AssetChunkDto chunk, string outputFilePath)
        {
            var payload = _assembler.AppendChunk(chunk);
            if (payload.Length == 0)
            {
                return;
            }

            _assembler.SaveToFile(payload, outputFilePath);
        }
    }
}
