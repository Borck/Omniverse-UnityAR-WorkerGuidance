using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Guidance.Runtime
{
    /// <summary>
    /// Downloads and caches target payload files by immutable target version.
    /// </summary>
    public sealed class TargetPayloadCache
    {
        private readonly string _cacheRoot;

        public TargetPayloadCache(string cacheRoot = null)
        {
            _cacheRoot = string.IsNullOrEmpty(cacheRoot)
                ? Path.Combine(Application.persistentDataPath, "guidance-target-cache")
                : cacheRoot;
            Directory.CreateDirectory(_cacheRoot);
        }

        public bool TryGetCachedFile(string targetVersion, string fileName, out string fullPath)
        {
            fullPath = GetTargetPath(targetVersion, fileName);
            return File.Exists(fullPath);
        }

        /// <summary>Returns the full cache path for a target file without checking existence.</summary>
        public string GetCachePath(string targetVersion, string fileName)
        {
            return GetTargetPath(targetVersion, fileName);
        }

        public IEnumerator GetOrDownloadFile(
            string url,
            string targetVersion,
            string fileName,
            Action<string> onReady,
            Action<string> onError)
        {
            if (string.IsNullOrEmpty(url))
            {
                onReady?.Invoke(string.Empty);
                yield break;
            }

            if (TryGetCachedFile(targetVersion, fileName, out var cachedPath))
            {
                onReady?.Invoke(cachedPath);
                yield break;
            }

            using var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"Target payload download failed: {request.error}");
                yield break;
            }

            var outputPath = GetTargetPath(targetVersion, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? _cacheRoot);
            File.WriteAllBytes(outputPath, request.downloadHandler.data);
            onReady?.Invoke(outputPath);
        }

        private string GetTargetPath(string targetVersion, string fileName)
        {
            var safeVersion = (targetVersion ?? "unknown").Replace(":", "_");
            return Path.Combine(_cacheRoot, safeVersion, fileName);
        }
    }
}
