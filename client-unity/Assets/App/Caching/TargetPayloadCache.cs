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

        /// <summary>
        /// Downloads both the paired .xml and .dat files for a Vuforia Model Target.
        /// <paramref name="datUrl"/> is the .dat URL the manifest provides; the matching
        /// .xml URL is derived by swapping the extension. Both files end up in the same
        /// cache folder so Vuforia can resolve the pair from the .xml path.
        /// </summary>
        public IEnumerator GetOrDownloadTargetPair(
            string primaryUrl,
            string targetVersion,
            string primaryFileName,
            Action<string, string> onReady,
            Action<string> onError)
        {
            if (string.IsNullOrEmpty(primaryUrl) || string.IsNullOrEmpty(primaryFileName))
            {
                onReady?.Invoke(string.Empty, string.Empty);
                yield break;
            }

            var xmlFileName = Path.ChangeExtension(primaryFileName, ".xml");
            var datFileName = Path.ChangeExtension(primaryFileName, ".dat");
            var baseUrlNoExt = primaryUrl.Substring(0, primaryUrl.Length - Path.GetExtension(primaryUrl).Length);
            var xmlUrl = baseUrlNoExt + ".xml";
            var datUrl = baseUrlNoExt + ".dat";

            string xmlPath = null;
            string datPath = null;
            string pairError = null;

            yield return GetOrDownloadFile(
                xmlUrl, targetVersion, xmlFileName,
                onReady: p => xmlPath = p,
                onError: e => pairError = e
            );
            if (!string.IsNullOrEmpty(pairError))
            {
                onError?.Invoke(pairError);
                yield break;
            }

            yield return GetOrDownloadFile(
                datUrl, targetVersion, datFileName,
                onReady: p => datPath = p,
                onError: e => pairError = e
            );
            if (!string.IsNullOrEmpty(pairError))
            {
                onError?.Invoke(pairError);
                yield break;
            }

            onReady?.Invoke(xmlPath, datPath);
        }

        private string GetTargetPath(string targetVersion, string fileName)
        {
            var safeVersion = (targetVersion ?? "unknown").Replace(":", "_");
            return Path.Combine(_cacheRoot, safeVersion, fileName);
        }
    }
}
