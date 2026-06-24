using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Renders the "FOV Tuning" section of the ControlDrawer accordion. No own
    /// window -- ControlDrawer calls <see cref="DrawContent"/>. Edits the
    /// CameraFovOverride on Camera.main (which persists its own state).
    /// </summary>
    public sealed class FovTunerPanel : MonoBehaviour
    {
        private CameraFovOverride _target;

        private void EnsureTarget()
        {
            if (_target != null) return;
            var cam = Camera.main;
            if (cam == null) return;
            _target = cam.GetComponent<CameraFovOverride>();
            if (_target == null) _target = cam.gameObject.AddComponent<CameraFovOverride>();
        }

        public void DrawContent()
        {
            EnsureTarget();
            if (_target == null) { GUILayout.Label("No main camera."); return; }

            var bh = GUILayout.Height(ImguiTheme.ControlHeight);

            GUILayout.Label($"FOV: {_target.FovDegrees:F1}°   (native ~37°)");

            var newFov = GUILayout.HorizontalSlider(
                _target.FovDegrees,
                CameraFovOverride.MinFovDegrees,
                CameraFovOverride.MaxFovDegrees,
                GUILayout.Height(ImguiTheme.ControlHeight));
            if (!Mathf.Approximately(newFov, _target.FovDegrees))
                _target.FovDegrees = newFov;

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("12°", bh)) _target.FovDegrees = 12f;
            if (GUILayout.Button("18°", bh)) _target.FovDegrees = 18f;
            if (GUILayout.Button("30°", bh)) _target.FovDegrees = 30f;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            var on = GUILayout.Toggle(_target.OverrideEnabled, " Zoom On", bh);
            if (on != _target.OverrideEnabled)
                _target.OverrideEnabled = on;
        }
    }
}
