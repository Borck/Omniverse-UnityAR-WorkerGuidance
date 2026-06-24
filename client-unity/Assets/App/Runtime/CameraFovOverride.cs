using UnityEngine;
using UnityEngine.Rendering;

namespace Guidance.Runtime
{
    /// <summary>
    /// On-glass scope zoom for the Vuzix M4000.
    ///
    /// Modulates Vuforia's projection matrix in URP's beginCameraRendering
    /// callback: reads whatever Vuforia just wrote, scales only m[0,0] and
    /// m[1,1] by the ratio of half-angle tangents to reach the target vertical
    /// FOV, leaves every other element (principal-point offset, sign
    /// convention, near/far) untouched. URP RenderGraph and Vuforia see exactly
    /// the matrix structure they expect, so neither the Y-flip nor the
    /// off-axis shift that earlier approaches produced can happen.
    ///
    /// OverrideEnabled defaults true: the projection-path FOV has been
    /// validated on glass as giving correct AR registration for the assembly
    /// workflow. Toggle OFF to hand the camera back to Vuforia for comparison
    /// or if a future Unity/URP update breaks rendering. The toggle is NOT
    /// persisted across launches -- each launch starts ON. FovDegrees IS
    /// persisted so the slider remembers across sessions.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFovOverride : MonoBehaviour
    {
        public const string PrefFovDeg = "guidance.fovOverride.deg";

        // Calibrated for M4000: Vuforia's native camera vfov on this device is
        // ~37°. The slider max equals that native value, so the TOP of the
        // slider == Vuforia's native projection (scale ~1.0, no zoom); every
        // value below it zooms in. Default 18° gives ~2.1x zoom out of the box.
        public const float MinFovDegrees     = 8f;
        public const float MaxFovDegrees     = 37f;
        public const float DefaultFovDegrees = 18f;

        [SerializeField] private bool overrideEnabled = true;
        [SerializeField, Range(MinFovDegrees, MaxFovDegrees)] private float fovDegrees = DefaultFovDegrees;

        private Camera _cam;
        private bool _hasLoggedVfov;

        /// <summary>
        /// Tracked fixture anchor whose on-screen position should stay fixed
        /// while zooming. Set by AppBootstrap each frame to the active Vuforia
        /// observer transform. When non-null and in front of the camera, the
        /// magnification is centered on it (no off-axis drift); when null, the
        /// zoom pivots on the principal point (slight outward flow).
        /// </summary>
        public Transform AnchorTransform { get; set; }

        public bool OverrideEnabled
        {
            get => overrideEnabled;
            set
            {
                if (overrideEnabled == value) return;
                overrideEnabled = value;
                // Hand projection back to Unity/Vuforia when turning off.
                if (!overrideEnabled && _cam != null)
                    _cam.ResetProjectionMatrix();
            }
        }

        public float FovDegrees
        {
            get => fovDegrees;
            set
            {
                var clamped = Mathf.Clamp(value, MinFovDegrees, MaxFovDegrees);
                if (Mathf.Approximately(clamped, fovDegrees)) return;
                fovDegrees = clamped;
                PersistFovPref();
            }
        }

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            fovDegrees = Mathf.Clamp(
                PlayerPrefs.GetFloat(PrefFovDeg, DefaultFovDegrees),
                MinFovDegrees, MaxFovDegrees);
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            if (_cam != null) _cam.ResetProjectionMatrix();
        }

        // Scale Vuforia's matrix in place: only m[0,0] and m[1,1] change, every
        // other element is preserved exactly. Vuforia's m[1,1] is negative (its
        // sign convention); scaling by a positive factor keeps it negative, so
        // URP RenderGraph treats our override identically to Vuforia's own
        // writes -- no Y-flip path is exercised.
        private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _cam || !overrideEnabled) return;

            var p = cam.projectionMatrix;
            float absM11 = Mathf.Abs(p.m11);
            if (absM11 < 1e-6f) return;

            float currentTanHalf = 1f / absM11;
            float targetTanHalf  = Mathf.Tan(fovDegrees * 0.5f * Mathf.Deg2Rad);
            if (targetTanHalf < 1e-6f) return;

            float scale = currentTanHalf / targetTanHalf;

            float origM00 = p.m00;
            float origM11 = p.m11;

            p.m00 = origM00 * scale;
            p.m11 = origM11 * scale;

            // Fixture-anchored magnification: nudge the principal point so the
            // anchor's screen position is invariant under the m00/m11 scaling.
            // Derivation: ndc.x = -m00*(X/Z) - m02. Requiring ndc.x unchanged
            // after m00 -> scale*m00 gives m02' = m02 + m00*(1-scale)*(X/Z).
            // Without this the zoom pivots on the principal point and the
            // (off-center) fixture drifts toward the screen edge.
            if (AnchorTransform != null)
            {
                Vector3 viewPos = cam.worldToCameraMatrix.MultiplyPoint3x4(AnchorTransform.position);
                if (viewPos.z < -1e-3f) // strictly in front of the camera
                {
                    float u = viewPos.x / viewPos.z;
                    float v = viewPos.y / viewPos.z;
                    p.m02 += origM00 * (1f - scale) * u;
                    p.m12 += origM11 * (1f - scale) * v;
                }
            }

            cam.projectionMatrix = p;

            // One-shot diagnostic so we can see Vuforia's actual native vfov
            // on this device (vs our guess) and the resulting zoom factor.
            if (!_hasLoggedVfov)
            {
                float vfovDeg = 2f * Mathf.Atan(currentTanHalf) * Mathf.Rad2Deg;
                Debug.Log($"[CameraFovOverride] Vuforia native vfov={vfovDeg:F1}°, target={fovDegrees:F1}°, scale={scale:F2}x");
                _hasLoggedVfov = true;
            }
        }

        private void PersistFovPref()
        {
            PlayerPrefs.SetFloat(PrefFovDeg, fovDegrees);
            PlayerPrefs.Save();
        }
    }
}
