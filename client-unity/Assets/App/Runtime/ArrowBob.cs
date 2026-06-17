using UnityEngine;

namespace App.Runtime
{
    /// <summary>
    /// Procedural "tap here" motion for a step arrow — a gentle bob along a local axis plus an
    /// optional scale pulse. Added at runtime by <see cref="StepArrowManager"/> when an arrow is
    /// marked animated, so it works regardless of whether the FBX shipped with animation data.
    /// The bob oscillates around <see cref="BaseLocalPosition"/>, which the manager keeps in sync
    /// with the configured placement (so live-preview tuning still works).
    /// </summary>
    public class ArrowBob : MonoBehaviour
    {
        [Tooltip("Bob distance in local units.")]
        public float amplitude = 0.015f;
        [Tooltip("Bob cycles per second.")]
        public float speed = 1.5f;
        [Tooltip("Local-space direction the arrow bobs along.")]
        public Vector3 axis = Vector3.up;
        [Tooltip("Extra scale added at the peak of the pulse (0 = no pulse).")]
        public float scalePulse = 0.05f;

        /// <summary>Centre of the bob; set by the spawner so it tracks the configured position.</summary>
        public Vector3 BaseLocalPosition { get; set; }

        private Vector3 _baseScale = Vector3.one;
        private bool _baseScaleCaptured;

        public void CaptureBaseScale(Vector3 scale)
        {
            _baseScale = scale;
            _baseScaleCaptured = true;
        }

        private void Update()
        {
            if (!_baseScaleCaptured) CaptureBaseScale(transform.localScale);

            float phase = Time.time * speed * Mathf.PI * 2f;
            float bob = Mathf.Sin(phase) * amplitude;
            transform.localPosition = BaseLocalPosition + axis.normalized * bob;

            if (scalePulse > 0f)
            {
                float pulse = 1f + (Mathf.Sin(phase) * 0.5f + 0.5f) * scalePulse;
                transform.localScale = _baseScale * pulse;
            }
        }
    }
}
