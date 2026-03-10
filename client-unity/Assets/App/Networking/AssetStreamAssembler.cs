using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Reassembles streamed asset chunks and persists completed payloads to disk.
    /// </summary>
    public sealed class AssetStreamAssembler
    {
        private readonly bool _supportsDraco;
        private readonly List<byte> _buffer = new();

        public AssetStreamAssembler(bool supportsDraco)
        {
            _supportsDraco = supportsDraco;
        }

        public byte[] AppendChunk(AssetChunkDto chunk)
        {
            if (chunk.AppliedCompression == AssetCompressionMode.Draco && !_supportsDraco)
            {
                throw new InvalidOperationException("Received Draco-compressed stream but client does not support Draco.");
            }

            _buffer.AddRange(chunk.Data);

            if (!chunk.IsLast)
            {
                return Array.Empty<byte>();
            }

            var payload = _buffer.ToArray();
            _buffer.Clear();

            // glTFast handles KHR_draco_mesh_compression in GLB when plugin support is present.
            return payload;
        }

        public void SaveToFile(byte[] payload, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            File.WriteAllBytes(path, payload);
            Debug.Log($"[AssetStreamAssembler] Wrote streamed asset: {path}");
        }
    }
}
