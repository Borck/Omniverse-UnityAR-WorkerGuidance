using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Minimal telemetry sink for runtime milestones and fault breadcrumbs.
    /// </summary>
    public sealed class TelemetryClient
    {
        public void TrackStepActivated(string jobId, string stepId, string partId)
        {
            Debug.Log($"[Telemetry] step.activated job={jobId} step={stepId} part={partId}");
        }

        public void TrackAssetCacheHit(string assetVersion, string fileName)
        {
            Debug.Log($"[Telemetry] asset.cache.hit version={assetVersion} file={fileName}");
        }

        public void TrackAssetDownloaded(string assetVersion, string fileName)
        {
            Debug.Log($"[Telemetry] asset.downloaded version={assetVersion} file={fileName}");
        }

        public void TrackFault(string code, string message)
        {
            Debug.LogWarning($"[Telemetry] fault code={code} message={message}");
        }
    }
}
