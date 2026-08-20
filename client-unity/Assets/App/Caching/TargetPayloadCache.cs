using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Guidance.Runtime
{
    /// <summary>
    /// Downloads and caches target payload files by immutable target version.
    /// Concurrent requests for the same file wait for the first download rather than
    /// each opening their own HTTP connection.
    /// </summary>
    public sealed class TargetPayloadCache
    {
        private readonly string _cacheRoot;
        private readonly HashSet<string> _inProgress = new HashSet<string>();

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

            var key = $"{targetVersion}/{fileName}";

            if (_inProgress.Contains(key))
            {
                yield return new WaitUntil(() => !_inProgress.Contains(key));
                if (TryGetCachedFile(targetVersion, fileName, out var waitedPath))
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
                onError?.Invoke(DescribeDownloadFailure(request, "Target payload"));
                yield break;
            }

            var outputPath = GetTargetPath(targetVersion, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? _cacheRoot);
            File.WriteAllBytes(outputPath, request.downloadHandler.data);
            _inProgress.Remove(key); // remove AFTER write so waiting coroutines find the file
            onReady?.Invoke(outputPath);
        }

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

            yield return GetOrDownloadFile(xmlUrl, targetVersion, xmlFileName,
                onReady: p => xmlPath = p, onError: e => pairError = e);
            if (!string.IsNullOrEmpty(pairError)) { onError?.Invoke(pairError); yield break; }

            yield return GetOrDownloadFile(datUrl, targetVersion, datFileName,
                onReady: p => datPath = p, onError: e => pairError = e);
            if (!string.IsNullOrEmpty(pairError)) { onError?.Invoke(pairError); yield break; }

            onReady?.Invoke(xmlPath, datPath);
        }

        private string GetTargetPath(string targetVersion, string fileName)
        {
            var safeVersion = (targetVersion ?? "unknown").Replace(":", "_");
            return Path.Combine(_cacheRoot, safeVersion, fileName);
        }

        /// <summary>
        /// Turns a UnityWebRequest failure into a human-readable reason instead of a
        /// raw transport string (e.g. "HTTP/2 ..."). Reports only the asset type and
        /// the failure category -- never the file name or version (kept private).
        /// </summary>
        private static string DescribeDownloadFailure(UnityWebRequest request, string kind)
        {
            switch (request.result)
            {
                case UnityWebRequest.Result.ProtocolError:
                    if (request.responseCode == 404)
                        return $"{kind} not found on server. No matching file exists in the server store.";
                    return $"Server rejected the {kind.ToLowerInvariant()} request: HTTP {request.responseCode}.";
                case UnityWebRequest.Result.ConnectionError:
                    return $"Cannot reach the server to download the {kind.ToLowerInvariant()}: {request.error}. " +
                           "Check that the server is running and reachable.";
                case UnityWebRequest.Result.DataProcessingError:
                    return $"Received the {kind.ToLowerInvariant()} but could not process the response: {request.error}.";
                default:
                    return $"{kind} download failed: {request.error}.";
            }
        }
    }
}
