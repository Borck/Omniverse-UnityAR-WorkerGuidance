using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Top-of-view IMGUI band for tuning <see cref="CameraFovOverride"/> on glass.
    /// Visibility is gated by <see cref="Visible"/> so the worker doesn't see this
    /// control during real assembly -- AppBootstrap flips it via the "Tune FOV"
    /// toggle. Edits the override component on Camera.main live; the override
    /// persists its own PlayerPrefs.
    /// </summary>
    public sealed class FovTunerPanel : MonoBehaviour
    {
        public bool Visible { get; set; }

        private CameraFovOverride _target;

        private void EnsureTarget()
        {
            if (_target != null) return;
            var cam = Camera.main;
            if (cam == null) return;
            _target = cam.GetComponent<CameraFovOverride>();
            if (_target == null) _target = cam.gameObject.AddComponent<CameraFovOverride>();
        }

        private void OnGUI()
        {
            if (!Visible) return;
            EnsureTarget();
            if (_target == null) return;

            ImguiTheme.Begin();

            const float w = 880f;
            const float h = 110f;
            var rect = new Rect((ImguiTheme.VirtualWidth - w) / 2f, 8f, w, h);

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.BeginHorizontal();

            GUILayout.Label($"FOV: {_target.FovDegrees:F1}°", GUILayout.Width(220f), GUILayout.Height(ImguiTheme.ControlHeight));

            var newFov = GUILayout.HorizontalSlider(
                _target.FovDegrees,
                CameraFovOverride.MinFovDegrees,
                CameraFovOverride.MaxFovDegrees,
                GUILayout.Height(ImguiTheme.ControlHeight),
                GUILayout.ExpandWidth(true));

            if (!Mathf.Approximately(newFov, _target.FovDegrees))
                _target.FovDegrees = newFov;

            GUILayout.Space(8);

            // Presets calibrated for M4000 (Vuforia native vfov ~37°):
            // 12° = heavy zoom (~3.2x), 18° = sensible default (~2.1x),
            // 30° = mild zoom (~1.25x). All real zoom-in values.
            if (GUILayout.Button("12°", GUILayout.Width(90f), GUILayout.Height(ImguiTheme.ControlHeight)))
                _target.FovDegrees = 12f;
            if (GUILayout.Button("18°", GUILayout.Width(90f), GUILayout.Height(ImguiTheme.ControlHeight)))
                _target.FovDegrees = 18f;
            if (GUILayout.Button("30°", GUILayout.Width(90f), GUILayout.Height(ImguiTheme.ControlHeight)))
                _target.FovDegrees = 30f;

            GUILayout.Space(12);

            var newEnabled = GUILayout.Toggle(_target.OverrideEnabled, " On", GUILayout.Width(110f), GUILayout.Height(ImguiTheme.ControlHeight));
            if (newEnabled != _target.OverrideEnabled)
                _target.OverrideEnabled = newEnabled;

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            ImguiTheme.End();
        }
    }
}
