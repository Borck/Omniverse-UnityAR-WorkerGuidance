using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Guidance.Runtime
{
    public sealed class StepAssetManifestClient
    {
        private readonly string _httpBaseUrl;

        public StepAssetManifestClient(string httpBaseUrl)
        {
            _httpBaseUrl = (httpBaseUrl ?? string.Empty).TrimEnd('/');
        }

        public IEnumerator ResolveStepAsset(
            string jobId,
            string stepId,
            Action<ResolvedStepAsset> onResolved,
            Action<string> onError)
        {
            ResolvedStepAssetBundle bundle = null;
            string error = null;
            yield return ResolveStepAssetWithNext(
                jobId,
                stepId,
                onResolved: value => bundle = value,
                onError: value => error = value
            );

            if (!string.IsNullOrEmpty(error))
            {
                onError?.Invoke(error);
                yield break;
            }

            if (bundle == null || bundle.Current == null)
            {
                onError?.Invoke($"Step {stepId} not found in manifest for job {jobId}");
                yield break;
            }

            onResolved?.Invoke(bundle.Current);
        }

        public IEnumerator ResolveStepAssetWithNext(
            string jobId,
            string stepId,
            Action<ResolvedStepAssetBundle> onResolved,
            Action<string> onError)
        {
            var manifestUrl = $"{_httpBaseUrl}/api/jobs/{jobId}/manifest";
            using var request = UnityWebRequest.Get(manifestUrl);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"Manifest download failed: {request.error}");
                yield break;
            }

            var payload = JsonUtility.FromJson<ManifestDto>(request.downloadHandler.text);
            if (payload == null || payload.steps == null)
            {
                onError?.Invoke("Manifest payload is invalid");
                yield break;
            }

            for (var i = 0; i < payload.steps.Length; i++)
            {
                var step = payload.steps[i];
                if (step.stepId != stepId)
                {
                    continue;
                }

                var current = new ResolvedStepAsset(
                    step.assetVersion,
                    ResolveUrl(step.glbUrl),
                    ResolveUrl(step.stepJsonUrl),
                    step.targetVersion,
                    ResolveUrl(step.targetUrl)
                );

                ResolvedStepAsset next = null;
                if (i + 1 < payload.steps.Length)
                {
                    var nextStep = payload.steps[i + 1];
                    next = new ResolvedStepAsset(
                        nextStep.assetVersion,
                        ResolveUrl(nextStep.glbUrl),
                        ResolveUrl(nextStep.stepJsonUrl),
                        nextStep.targetVersion,
                        ResolveUrl(nextStep.targetUrl)
                    );
                }

                onResolved?.Invoke(new ResolvedStepAssetBundle(current, next));
                yield break;
            }

            onError?.Invoke($"Step {stepId} not found in manifest for job {jobId}");
        }

        private string ResolveUrl(string maybeRelative)
        {
            if (string.IsNullOrEmpty(maybeRelative))
            {
                return string.Empty;
            }

            if (maybeRelative.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || maybeRelative.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return maybeRelative;
            }

            if (maybeRelative.StartsWith("/"))
            {
                return _httpBaseUrl + maybeRelative;
            }

            return _httpBaseUrl + "/" + maybeRelative;
        }

        [Serializable]
        private sealed class ManifestDto
        {
            public string jobId;
            public string workflowVersion;
            public StepDto[] steps;
        }

        [Serializable]
        private sealed class StepDto
        {
            public string stepId;
            public string partId;
            public string assetVersion;
            public string glbUrl;
            public string stepJsonUrl;
            public string targetVersion;
            public string targetUrl;
            public string compression;
        }
    }

    public sealed class ResolvedStepAsset
    {
        public string AssetVersion { get; }
        public string GlbUrl { get; }
        public string StepJsonUrl { get; }
        public string TargetVersion { get; }
        public string TargetUrl { get; }

        public ResolvedStepAsset(string assetVersion, string glbUrl, string stepJsonUrl, string targetVersion, string targetUrl)
        {
            AssetVersion = assetVersion;
            GlbUrl = glbUrl;
            StepJsonUrl = stepJsonUrl;
            TargetVersion = targetVersion;
            TargetUrl = targetUrl;
        }
    }

    public sealed class ResolvedStepAssetBundle
    {
        public ResolvedStepAsset Current { get; }
        public ResolvedStepAsset Next { get; }

        public ResolvedStepAssetBundle(ResolvedStepAsset current, ResolvedStepAsset next)
        {
            Current = current;
            Next = next;
        }
    }
}
