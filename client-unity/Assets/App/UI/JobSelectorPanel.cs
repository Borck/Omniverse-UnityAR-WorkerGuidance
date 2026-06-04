using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Picks the active job to run. Project is model-target only — one button,
    /// one job ID. Override the Inspector field to switch between jobs without
    /// touching code.
    /// </summary>
    public sealed class JobSelectorPanel : MonoBehaviour
    {
        [Tooltip("Job ID to load when the user taps Start. Must match a jobId in step-definitions.yaml.")]
        [SerializeField] private string jobId = "PU_Segment_Assembly";

        private AppBootstrap _bootstrap;
        private bool _visible;

        public void Show(AppBootstrap bootstrap)
        {
            _bootstrap = bootstrap;
            _visible = true;
        }

        private void OnGUI()
        {
            if (!_visible) return;

            const float w = 320f;
            const float h = 140f;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("<b>Ready to start</b>");
            GUILayout.Space(8);
            GUILayout.Label($"Job: {jobId}");
            GUILayout.Space(12);

            if (GUILayout.Button("Start", GUILayout.Height(48)))
            {
                _visible = false;
                _bootstrap.InitializeWithJob(jobId);
            }

            GUILayout.EndArea();
        }
    }
}
