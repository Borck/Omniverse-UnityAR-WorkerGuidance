using UnityEngine;

namespace Guidance.Runtime
{
    // Placeholder status UI for milestone M7. Replace with final canvas-based HUD later.
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

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(16, 16, 520, 190), GUI.skin.box);
            GUILayout.Label("Guidance Runtime Status");
            GUILayout.Label($"Connection: {_connectionState}");
            GUILayout.Label($"Step State: {_stepState}");
            GUILayout.Label($"Active Step: {_activeStep}");
            GUILayout.Label($"Active Part: {_activePart}");
            GUILayout.Label($"Instruction: {_instruction}");

            if (!string.IsNullOrEmpty(_warning))
            {
                GUILayout.Label($"Warning: {_warning}");
            }

            if (showControls && appBootstrap != null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Replay"))
                {
                    appBootstrap.ReplayActiveStep();
                }
                if (GUILayout.Button("Previous"))
                {
                    appBootstrap.PreviousStep();
                }
                if (GUILayout.Button("Confirm / Next"))
                {
                    appBootstrap.ConfirmActiveStep();
                }
                if (GUILayout.Button("Help"))
                {
                    appBootstrap.ShowHelp();
                }
                if (GUILayout.Button("Diagnostics"))
                {
                    appBootstrap.ExportDiagnosticsBundle();
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }
    }
}
