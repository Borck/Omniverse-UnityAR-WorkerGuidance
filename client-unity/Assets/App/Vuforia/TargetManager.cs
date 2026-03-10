using UnityEngine;

namespace Guidance.Runtime
{
    public sealed class TargetManager
    {
        private readonly float _positionSmoothing;
        private readonly float _rotationSmoothing;

        public string ActiveTargetId { get; private set; } = string.Empty;
        public string ActiveTargetVersion { get; private set; } = string.Empty;
        public string ActiveTargetPayloadPath { get; private set; } = string.Empty;
        public bool IsTrackingAcquired { get; private set; }
        public Vector3 SmoothedWorldPosition { get; private set; }
        public Quaternion SmoothedWorldRotation { get; private set; } = Quaternion.identity;

        public TargetManager(float positionSmoothing = 0.2f, float rotationSmoothing = 0.2f)
        {
            _positionSmoothing = Mathf.Clamp01(positionSmoothing);
            _rotationSmoothing = Mathf.Clamp01(rotationSmoothing);
        }

        public void ActivateTarget(string targetId, string targetVersion, string targetPayloadPath = "")
        {
            ActiveTargetId = targetId ?? string.Empty;
            ActiveTargetVersion = targetVersion ?? string.Empty;
            ActiveTargetPayloadPath = targetPayloadPath ?? string.Empty;
            IsTrackingAcquired = false;
            SmoothedWorldPosition = Vector3.zero;
            SmoothedWorldRotation = Quaternion.identity;
            Debug.Log($"[TargetManager] Activated target {ActiveTargetId} (version={ActiveTargetVersion}, payload={ActiveTargetPayloadPath})");
        }

        public void UpdateTrackingPose(Vector3 observedPosition, Quaternion observedRotation, bool trackingAcquired)
        {
            if (trackingAcquired)
            {
                if (!IsTrackingAcquired)
                {
                    SmoothedWorldPosition = observedPosition;
                    SmoothedWorldRotation = observedRotation;
                }
                else
                {
                    SmoothedWorldPosition = Vector3.Lerp(SmoothedWorldPosition, observedPosition, _positionSmoothing);
                    SmoothedWorldRotation = Quaternion.Slerp(SmoothedWorldRotation, observedRotation, _rotationSmoothing);
                }
            }

            IsTrackingAcquired = trackingAcquired;
        }

        public float GetTrackingHintSignedAngleDegrees(Camera camera)
        {
            if (camera == null || IsTrackingAcquired || string.IsNullOrEmpty(ActiveTargetId))
            {
                return 0f;
            }

            var toTarget = SmoothedWorldPosition - camera.transform.position;
            var cameraForward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            var flatToTarget = Vector3.ProjectOnPlane(toTarget, Vector3.up);

            if (flatToTarget.sqrMagnitude < 0.0001f || cameraForward.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            return Vector3.SignedAngle(cameraForward.normalized, flatToTarget.normalized, Vector3.up);
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
            ActiveTargetPayloadPath = string.Empty;
            IsTrackingAcquired = false;
            SmoothedWorldPosition = Vector3.zero;
            SmoothedWorldRotation = Quaternion.identity;
        }
    }
}
