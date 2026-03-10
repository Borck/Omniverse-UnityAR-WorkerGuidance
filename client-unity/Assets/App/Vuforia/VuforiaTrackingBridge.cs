using UnityEngine;

#if VUFORIA_ENGINE
using Vuforia;
#endif

namespace Guidance.Runtime
{
    /// <summary>
    /// Bridges Vuforia observer status events into runtime tracking callbacks.
    /// </summary>
    public sealed class VuforiaTrackingBridge : MonoBehaviour
    {
        [SerializeField] private AppBootstrap appBootstrap;

#if VUFORIA_ENGINE
        [SerializeField] private ObserverBehaviour observerBehaviour;
#endif

        private void Awake()
        {
            if (appBootstrap == null)
            {
                appBootstrap = FindFirstObjectByType<AppBootstrap>();
            }
        }

#if VUFORIA_ENGINE
        private void OnEnable()
        {
            if (observerBehaviour != null)
            {
                observerBehaviour.OnTargetStatusChanged += HandleTargetStatusChanged;
            }
        }

        private void OnDisable()
        {
            if (observerBehaviour != null)
            {
                observerBehaviour.OnTargetStatusChanged -= HandleTargetStatusChanged;
            }
        }

        private void HandleTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
        {
            if (appBootstrap == null)
            {
                return;
            }

            var trackingAcquired =
                status.Status == Status.TRACKED
                || status.Status == Status.EXTENDED_TRACKED
                || status.Status == Status.LIMITED;

            var poseTransform = behaviour != null ? behaviour.transform : transform;
            appBootstrap.OnTargetTrackingUpdated(
                poseTransform.position,
                poseTransform.rotation,
                trackingAcquired
            );
        }
#else
        // Editor/test fallback when Vuforia package is not installed.
        public void InjectTrackingSample(Transform observedPose, bool trackingAcquired)
        {
            if (appBootstrap == null)
            {
                return;
            }

            var poseTransform = observedPose != null ? observedPose : transform;
            appBootstrap.OnTargetTrackingUpdated(
                poseTransform.position,
                poseTransform.rotation,
                trackingAcquired
            );
        }
#endif
    }
}
