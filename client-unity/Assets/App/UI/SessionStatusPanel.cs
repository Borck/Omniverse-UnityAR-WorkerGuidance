using UnityEngine;

namespace Guidance.Runtime
{
    // Placeholder status UI for milestone M7. Replace with final canvas-based HUD later.
    public sealed class SessionStatusPanel : MonoBehaviour
    {
        [SerializeField] private bool visible = true;

        private SessionConnectionState _connectionState = SessionConnectionState.Disconnected;
        private StepCoordinatorState _stepState = StepCoordinatorState.Idle;
        private string _activeStep = "-";
        private string _activePart = "-";

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

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(16, 16, 380, 120), GUI.skin.box);
            GUILayout.Label("Guidance Runtime Status");
            GUILayout.Label($"Connection: {_connectionState}");
            GUILayout.Label($"Step State: {_stepState}");
            GUILayout.Label($"Active Step: {_activeStep}");
            GUILayout.Label($"Active Part: {_activePart}");
            GUILayout.EndArea();
        }
    }
}
