using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Lightweight immediate-mode HUD showing session/step status and operator actions.
    /// </summary>
    public sealed class SessionStatusPanel : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private bool showControls = true;
        [SerializeField] private AppBootstrap appBootstrap;

        private SessionConnectionState _connectionState = SessionConnectionState.Disconnected;
        private StepCoordinatorState _stepState = StepCoordinatorState.Idle;
        private string _activeStep = "-";
        private string _activePart = "-";
        private string _instruction = "-";
        private string _warning = string.Empty;

        private string _pipelineStatus = string.Empty;
        private string _targetStatus = string.Empty;
        private string _transportMode = string.Empty;
        private bool? _imageTargetFound = null;
        private bool _showLogPanel = false;

        private void Awake()
        {
            if (appBootstrap == null)
            {
                appBootstrap = FindFirstObjectByType<AppBootstrap>();
            }
        }

        public void SetConnectionState(SessionConnectionState state)
        {
            _connectionState = state;
        }

        public void SetStepState(StepCoordinatorState state)
        {
            _stepState = state;
        }

        public void SetActiveStep(string stepId, string partId)
        {
            _activeStep = string.IsNullOrEmpty(stepId) ? "-" : stepId;
            _activePart = string.IsNullOrEmpty(partId) ? "-" : partId;
        }

        public void SetInstruction(string instruction)
        {
            _instruction = string.IsNullOrEmpty(instruction) ? "-" : instruction;
        }

        public void SetWarning(string warning)
        {
            _warning = warning ?? string.Empty;
        }
        public void SetPipelineStatus(string status)
        {
            _pipelineStatus = status ?? string.Empty;
        }

        public void SetTargetStatus(string status)
        {
            _targetStatus = status ?? string.Empty;
        }

        public void SetTransportMode(string mode)
        {
            _transportMode = mode ?? string.Empty;
        }

        public void SetImageTargetFound(bool found)
        {
            _imageTargetFound = found;
        }

        public void ClearImageTargetFound()
        {
            _imageTargetFound = null;
        }

        private void OnGUI()
        {
            if (!visible) return;

            var panelWidth = Mathf.Min(Screen.width - 16f, 340f);
            var btnWidth2 = GUILayout.Width((panelWidth - 24) / 2f);
            var btnHeight = GUILayout.Height(30);

            var extraLines = string.IsNullOrEmpty(_warning) ? 0 : 1;
            var panelHeight = 170f + extraLines * 26f;

            // Main status panel (top-left)
            GUILayout.BeginArea(new Rect(8, 8, panelWidth, panelHeight), GUI.skin.box);
            GUILayout.Label("<b>Guidance Runtime Status</b>");
            GUILayout.Label($"Connection: {_connectionState}");
            GUILayout.Label($"Instruction: {_instruction}");

            if (!string.IsNullOrEmpty(_warning))
                GUILayout.Label($"Warning: {_warning}");

            if (showControls && appBootstrap != null)
            {
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Replay", btnWidth2, btnHeight)) appBootstrap.ReplayActiveStep();
                if (GUILayout.Button("Previous", btnWidth2, btnHeight)) appBootstrap.PreviousStep();
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Confirm / Next", btnWidth2, btnHeight)) appBootstrap.ConfirmActiveStep();
                if (GUILayout.Button("Switch Mode", btnWidth2, btnHeight)) appBootstrap.ReturnToJobSelector();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();

            // Standalone Log/Status toggle button (bottom-left)
            GUILayout.BeginArea(new Rect(8, Screen.height - 34f, 110f, 26f));
            if (GUILayout.Button("Log / Status"))
                _showLogPanel = !_showLogPanel;
            GUILayout.EndArea();

            // Log popup — appears just above the toggle button
            if (_showLogPanel)
            {
                var logLines = 3
                    + (string.IsNullOrEmpty(_pipelineStatus) ? 0 : 1)
                    + (string.IsNullOrEmpty(_targetStatus) ? 0 : 1)
                    + (_imageTargetFound.HasValue ? 1 : 0);
                var logHeight = logLines * 26f + 20f;

                GUILayout.BeginArea(new Rect(8, Screen.height - logHeight - 42f, 280f, logHeight), GUI.skin.box);
                GUILayout.Label("<b>Log / Status</b>");
                GUILayout.Label($"Step State: {_stepState}");
                GUILayout.Label($"Active Step: {_activeStep}");
                if (!string.IsNullOrEmpty(_pipelineStatus))
                    GUILayout.Label($"GLB: {_pipelineStatus}");
                if (!string.IsNullOrEmpty(_targetStatus))
                    GUILayout.Label($"Target: {_targetStatus}");
                if (_imageTargetFound.HasValue)
                    GUILayout.Label($"Target Tracked: {(_imageTargetFound.Value ? "YES" : "NO")}");
                GUILayout.EndArea();
            }
        }
    }
}
