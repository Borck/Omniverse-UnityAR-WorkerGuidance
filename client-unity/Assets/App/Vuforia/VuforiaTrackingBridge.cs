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

        /// <summary>
        /// Attaches the bridge to an observer created at runtime after the Model Target
        /// database was loaded from the downloaded .xml/.dat pair. Safe to call repeatedly;
        /// replaces any previously bound observer.
        /// </summary>
        public void AssignObserver(ObserverBehaviour newObserver)
        {
            if (observerBehaviour == newObserver)
            {
                return;
            }

            if (observerBehaviour != null)
            {
                observerBehaviour.OnTargetStatusChanged -= HandleTargetStatusChanged;
            }

            observerBehaviour = newObserver;

            if (observerBehaviour != null && isActiveAndEnabled)
            {
                observerBehaviour.OnTargetStatusChanged += HandleTargetStatusChanged;
            }
        }

        /// <summary>
        /// Single source of truth for "is the pose solid enough to show
        /// world-locked content" (fail-safe / option A). Only TRACKED and
        /// LIMITED count. EXTENDED_TRACKED — the device tracker maintaining a
        /// target that is NOT currently in the camera view — deliberately does
        /// NOT count, nor does NO_POSE: the extended pose drifts and would
        /// otherwise look head-locked when the worker glances away from the
        /// fixture. We hide the hologram in those states and prompt a re-aim.
        /// </summary>
        public static bool IsSolidPose(Status status)
            => status == Status.TRACKED || status == Status.LIMITED;

        private void HandleTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
        {
            if (appBootstrap == null)
            {
                return;
            }

            // Log status + StatusInfo so we can diagnose drift (WRONG_SCALE = model target
            // database scale doesn't match the physical object's real-world size).
            Debug.Log($"[VuforiaTrackingBridge] Status={status.Status} StatusInfo={status.StatusInfo} target={behaviour?.TargetName}");

            var trackingAcquired = IsSolidPose(status.Status);

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
