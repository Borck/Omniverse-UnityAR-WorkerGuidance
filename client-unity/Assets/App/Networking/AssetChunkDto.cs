namespace Guidance.Runtime
{
    /// <summary>
    /// Represents one chunk in a streamed step asset transfer.
    /// </summary>
    public sealed class AssetChunkDto
    {
        public string JobId { get; init; } = string.Empty;
        public string StepId { get; init; } = string.Empty;
        public string AssetVersion { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public AssetCompressionMode AppliedCompression { get; init; } = AssetCompressionMode.Unspecified;
        public int ChunkIndex { get; init; }
        public byte[] Data { get; init; } = System.Array.Empty<byte>();
        public bool IsLast { get; init; }
    }
}
