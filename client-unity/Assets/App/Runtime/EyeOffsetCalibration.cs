using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Worker-calibrated eye/camera offset for optical see-through parallax
    /// correction on the M4000. The tracking camera sits ~5.5 cm from the eye,
    /// and Vuforia renders from the camera viewpoint, so holograms land offset
    /// from the real fixture -- worse the closer the fixture is.
    ///
    /// The stored value is a CAMERA-SPACE vector (x = right, y = up, z = forward,
    /// in metres). AppBootstrap applies it to the tracked content each frame as a
    /// viewpoint-equivalent shift. Because a fixed camera-space content shift is
    /// mathematically identical to translating the rendering viewpoint, the
    /// correction is distance-accurate at ALL distances automatically -- the
    /// perspective projection produces the right parallax for near and far. The
    /// worker calibrates the value once on glass and it persists (PlayerPrefs).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class EyeOffsetCalibration : MonoBehaviour
    {
        public const string PrefX = "guidance.eyeOffset.x";
        public const string PrefY = "guidance.eyeOffset.y";
        public const string PrefZ = "guidance.eyeOffset.z";

        // The M4000's camera lens sits ~4 cm IN FRONT of the eye, so the eye is
        // 4 cm behind the camera along its look direction. Rendering from the eye
        // viewpoint is equivalent to shifting content +4 cm forward (+Z in camera
        // space). Seeding this means the depth (Z) component is approximately
        // correct out of the box, so the worker only fine-tunes the lateral X/Y
        // -- which makes the calibration far less angle-sensitive.
        public const float DefaultForwardMeters = 0.04f;

        [SerializeField] private Vector3 offsetMeters = new Vector3(0f, 0f, DefaultForwardMeters);

        public Vector3 OffsetMeters
        {
            get => offsetMeters;
            set { offsetMeters = value; Persist(); }
        }

        public void Nudge(float dx, float dy, float dz)
        {
            offsetMeters += new Vector3(dx, dy, dz);
            Persist();
        }

        public void ResetOffset()
        {
            // Reset to the seeded depth (not full zero) so the physical Z stays.
            offsetMeters = new Vector3(0f, 0f, DefaultForwardMeters);
            Persist();
        }

        private void Awake()
        {
            offsetMeters = new Vector3(
                PlayerPrefs.GetFloat(PrefX, 0f),
                PlayerPrefs.GetFloat(PrefY, 0f),
                PlayerPrefs.GetFloat(PrefZ, DefaultForwardMeters));
        }

        private void Persist()
        {
            PlayerPrefs.SetFloat(PrefX, offsetMeters.x);
            PlayerPrefs.SetFloat(PrefY, offsetMeters.y);
            PlayerPrefs.SetFloat(PrefZ, offsetMeters.z);
            PlayerPrefs.Save();
        }
    }
}
