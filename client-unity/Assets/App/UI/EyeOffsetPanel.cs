using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Renders the "Eye Calibrate" section of the ControlDrawer accordion: a
    /// guided two-distance routine that pins the full 3D camera->eye offset so
    /// the hologram stays aligned at any distance and angle. No own window --
    /// ControlDrawer calls <see cref="DrawContent"/>; <see cref="ResetGuide"/>
    /// restarts the guide when the section is opened.
    /// </summary>
    public sealed class EyeOffsetPanel : MonoBehaviour
    {
        private const float StepMeters = 0.005f; // 5 mm per tap

        private int _step; // 0 = close/lateral, 1 = far/depth, 2 = done
        private EyeOffsetCalibration _target;

        public void ResetGuide() => _step = 0;

        private void EnsureTarget()
        {
            if (_target != null) return;
            var cam = Camera.main;
            if (cam == null) return;
            _target = cam.GetComponent<EyeOffsetCalibration>();
            if (_target == null) _target = cam.gameObject.AddComponent<EyeOffsetCalibration>();
        }

        private static GUILayoutOption H => GUILayout.Height(ImguiTheme.ControlHeight);

        public void DrawContent()
        {
            EnsureTarget();
            if (_target == null) { GUILayout.Label("No main camera."); return; }

            var o = _target.OffsetMeters;
            GUILayout.Label($"Offset  X {o.x * 100f:F1}  Y {o.y * 100f:F1}  Z {o.z * 100f:F1} cm");
            GUILayout.Space(4);

            switch (_step)
            {
                case 0: DrawStepClose(); break;
                case 1: DrawStepFar(); break;
                default: DrawDone(); break;
            }
        }

        private void DrawStepClose()
        {
            GUILayout.Label("Step 1/2 — Stand CLOSE & square. Move the hologram onto the real part.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◄ Left",  H)) _target.Nudge(-StepMeters, 0f, 0f);
            if (GUILayout.Button("Right ►", H)) _target.Nudge( StepMeters, 0f, 0f);
            if (GUILayout.Button("▲ Up",    H)) _target.Nudge(0f,  StepMeters, 0f);
            if (GUILayout.Button("▼ Down",  H)) _target.Nudge(0f, -StepMeters, 0f);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset", H)) _target.ResetOffset();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Next ▶", GUILayout.Width(180f), H)) _step = 1;
            GUILayout.EndHorizontal();
        }

        private void DrawStepFar()
        {
            GUILayout.Label("Step 2/2 — Step BACK ~2x. Adjust depth until it stays locked.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Near −", H)) _target.Nudge(0f, 0f, -StepMeters);
            if (GUILayout.Button("Far +",  H)) _target.Nudge(0f, 0f,  StepMeters);
            if (GUILayout.Button("◄ Left",  H)) _target.Nudge(-StepMeters, 0f, 0f);
            if (GUILayout.Button("Right ►", H)) _target.Nudge( StepMeters, 0f, 0f);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀ Back", H)) _step = 0;
            if (GUILayout.Button("Reset", H)) _target.ResetOffset();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Finish ✓", GUILayout.Width(180f), H)) _step = 2;
            GUILayout.EndHorizontal();
        }

        private void DrawDone()
        {
            GUILayout.Label("Calibration saved. Stays aligned at any distance. Recalibrate if it drifts at an angle.");
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Recalibrate", H)) _step = 0;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset", H)) { _target.ResetOffset(); _step = 0; }
            GUILayout.EndHorizontal();
        }
    }
}
