using UnityEngine;

namespace Guidance.Runtime
{
    public sealed class TargetManager
    {
        public string ActiveTargetId { get; private set; } = string.Empty;
        public string ActiveTargetVersion { get; private set; } = string.Empty;

        public void ActivateTarget(string targetId, string targetVersion)
        {
            ActiveTargetId = targetId ?? string.Empty;
            ActiveTargetVersion = targetVersion ?? string.Empty;
            Debug.Log($"[TargetManager] Activated target {ActiveTargetId} (version={ActiveTargetVersion})");
        }

        public void DeactivateTarget()
        {
            if (string.IsNullOrEmpty(ActiveTargetId))
            {
                return;
            }

            Debug.Log($"[TargetManager] Deactivated target {ActiveTargetId}");
            ActiveTargetId = string.Empty;
            ActiveTargetVersion = string.Empty;
        }
    }
}
