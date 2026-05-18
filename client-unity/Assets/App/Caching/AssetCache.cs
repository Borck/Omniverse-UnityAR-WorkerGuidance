using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Guidance.Runtime
{
    /// <summary>
    /// Downloads and caches immutable step assets by asset version.
    /// Concurrent requests for the same file wait for the first download rather than
    /// each opening their own HTTP connection.
    /// </summary>
    public sealed class AssetCache
    {
        private readonly string _cacheRoot;
        private readonly HashSet<string> _inProgress = new HashSet<string>();

        public AssetCache(string cacheRoot = null)
        {
            _cacheRoot = string.IsNullOrEmpty(cacheRoot)
                ? Path.Combine(Application.persistentDataPath, "guidance-cache")
                : cacheRoot;
            Directory.CreateDirectory(_cacheRoot);
        }

        public bool TryGetCachedFile(string assetVersion, string fileName, out string fullPath)
        {
            fullPath = GetAssetPath(assetVersion, fileName);
            return File.Exists(fullPath);
        }

        public IEnumerator GetOrDownloadFile(
            string url,
            string assetVersion,
            string fileName,
            Action<string> onReady,
            Action<string> onError)
        {
            if (TryGetCachedFile(assetVersion, fileName, out var cachedPath))
            {
                onReady?.Invoke(cachedPath);
                yield break;
            }

            var key = $"{assetVersion}/{fileName}";

            if (_inProgress.Contains(key))
            {
                yield return new WaitUntil(() => !_inProgress.Contains(key));
                if (TryGetCachedFile(assetVersion, fileName, out var waitedPath))
                    onReady?.Invoke(waitedPath);
                else
                    onError?.Invoke($"Concurrent download of {fileName} failed");
                yield break;
            }

            _inProgress.Add(key);

            using var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                _inProgress.Remove(key);
                onError?.Invoke($"Asset download failed: {request.error}");
                yield break;
            }

            var outputPath = GetAssetPath(assetVersion, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? _cacheRoot);
            File.WriteAllBytes(outputPath, request.downloadHandler.data);
            _inProgress.Remove(key); // remove AFTER write so waiting coroutines find the file
            onReady?.Invoke(outputPath);
        }

        private string GetAssetPath(string assetVersion, string fileName)
        {
            var safeVersion = (assetVersion ?? "unknown").Replace(":", "_");
            return Path.Combine(_cacheRoot, safeVersion, fileName);
        }
    }
}
