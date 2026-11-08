using UnityEngine;

namespace Guidance.Runtime
{
    public sealed class JobSelectorPanel : MonoBehaviour
    {
        [SerializeField] private string modelTargetJobId = "demonstrator-26-02-25";
        [SerializeField] private string imageTargetJobId = "demonstrator-26-02-25-img";

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
            const float h = 180f;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("<b>Select Demo Mode</b>");
            GUILayout.Space(12);

            if (GUILayout.Button("Model Target", GUILayout.Height(44)))
                Select(modelTargetJobId);

            GUILayout.Space(8);

            if (GUILayout.Button("Image Target", GUILayout.Height(44)))
                Select(imageTargetJobId);

            GUILayout.EndArea();
        }

        private void Select(string jobId)
        {
            _visible = false;
            _bootstrap.InitializeWithJob(jobId);
        }
    }
}
