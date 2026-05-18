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

        private void OnGUI()
        {
            if (!visible) return;

            var panelWidth = Mathf.Min(Screen.width - 16f, 340f);
            var btnWidth = GUILayout.Width((panelWidth - 20) / 3f);
            var btnHeight = GUILayout.Height(30);

            var extraLines = (string.IsNullOrEmpty(_warning) ? 0 : 1)
                           + (string.IsNullOrEmpty(_pipelineStatus) ? 0 : 1)
                           + (string.IsNullOrEmpty(_targetStatus) ? 0 : 1);
            var panelHeight = 265f + extraLines * 26f;

            GUILayout.BeginArea(new Rect(8, 8, panelWidth, panelHeight), GUI.skin.box);

            GUILayout.Label("<b>Guidance Runtime Status</b>");
            if (!string.IsNullOrEmpty(_transportMode))
                GUILayout.Label($"Transport: {_transportMode}");
            GUILayout.Label($"Connection: {_connectionState}");
            GUILayout.Label($"Step State: {_stepState}");
            GUILayout.Label($"Active Step: {_activeStep}");
            GUILayout.Label($"Active Part: {_activePart}");
            GUILayout.Label($"Instruction: {_instruction}");

            if (!string.IsNullOrEmpty(_warning))
                GUILayout.Label($"Warning: {_warning}");
            if (!string.IsNullOrEmpty(_pipelineStatus))
                GUILayout.Label($"GLB: {_pipelineStatus}");
            if (!string.IsNullOrEmpty(_targetStatus))
                GUILayout.Label($"Target: {_targetStatus}");

            if (showControls && appBootstrap != null)
            {
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Replay", btnWidth, btnHeight)) appBootstrap.ReplayActiveStep();
                if (GUILayout.Button("Previous", btnWidth, btnHeight)) appBootstrap.PreviousStep();
                if (GUILayout.Button("Confirm / Next", btnWidth, btnHeight)) appBootstrap.ConfirmActiveStep();
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Help", btnWidth, btnHeight)) appBootstrap.ShowHelp();
                if (GUILayout.Button("Diagnostics", btnWidth, btnHeight)) appBootstrap.ExportDiagnosticsBundle();
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }
    }
}
