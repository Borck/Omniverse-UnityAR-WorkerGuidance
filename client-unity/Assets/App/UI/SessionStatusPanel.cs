using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Holds session/step status and renders the "Runtime Status" section of the
    /// ControlDrawer accordion. It no longer draws its own window -- ControlDrawer
    /// calls <see cref="DrawContent"/> inside the drawer layout.
    /// </summary>
    public sealed class SessionStatusPanel : MonoBehaviour
    {
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
        private float _fixtureDistanceMeters = -1f; // < 0 = unknown / not tracked
        private bool _showReacquireHint = false;
        private GUIStyle _hintStyle;

        private void Awake()
        {
            if (appBootstrap == null)
                appBootstrap = FindFirstObjectByType<AppBootstrap>();
        }

        public void SetConnectionState(SessionConnectionState state) => _connectionState = state;
        public void SetStepState(StepCoordinatorState state) => _stepState = state;
        public void SetInstruction(string instruction) => _instruction = string.IsNullOrEmpty(instruction) ? "-" : instruction;
        public void SetWarning(string warning) => _warning = warning ?? string.Empty;
        public void SetPipelineStatus(string status) => _pipelineStatus = status ?? string.Empty;
        public void SetTargetStatus(string status) => _targetStatus = status ?? string.Empty;
        public void SetTransportMode(string mode) => _transportMode = mode ?? string.Empty;
        public void SetImageTargetFound(bool found) => _imageTargetFound = found;
        public void ClearImageTargetFound() => _imageTargetFound = null;
        /// <summary>Live camera→fixture distance in metres; pass a negative value when unknown/untracked.</summary>
        public void SetFixtureDistance(float meters) => _fixtureDistanceMeters = meters;
        /// <summary>Show/hide the always-on "re-aim at the fixture" overlay, shown while a job is active but the pose is not solidly tracked (fail-safe hologram hide).</summary>
        public void SetReacquireHint(bool show) => _showReacquireHint = show;

        public void SetActiveStep(string stepId, string partId)
        {
            _activeStep = string.IsNullOrEmpty(stepId) ? "-" : stepId;
            _activePart = string.IsNullOrEmpty(partId) ? "-" : partId;
        }

        /// <summary>Drawn by ControlDrawer inside the accordion (no own OnGUI).</summary>
        public void DrawContent()
        {
            var bh = GUILayout.Height(ImguiTheme.ControlHeight);

            GUILayout.Label($"Connection: {_connectionState}");
            GUILayout.Label($"Step: {_activeStep}    Part: {_activePart}");
            GUILayout.Label($"Instruction: {_instruction}");
            if (!string.IsNullOrEmpty(_warning))
                GUILayout.Label($"<b>Warning:</b> {_warning}");

            // Compact diagnostics line(s), only when present.
            if (!string.IsNullOrEmpty(_pipelineStatus))
                GUILayout.Label($"GLB: {_pipelineStatus}");
            if (!string.IsNullOrEmpty(_targetStatus))
                GUILayout.Label($"Target: {_targetStatus}");
            if (_imageTargetFound.HasValue)
                GUILayout.Label($"Tracked: {(_imageTargetFound.Value ? "YES" : "NO")}");
            GUILayout.Label(_fixtureDistanceMeters >= 0f
                ? $"Fixture: {_fixtureDistanceMeters:F2} m"
                : "Fixture: -- (not tracked)");

            if (showControls && appBootstrap != null)
            {
                GUILayout.Space(8);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("◀ Previous", bh)) appBootstrap.PreviousStep();
                if (GUILayout.Button("Replay", bh)) appBootstrap.ReplayActiveStep();
                if (GUILayout.Button("Next ▶", bh)) appBootstrap.ConfirmActiveStep();
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Switch Mode", bh)) appBootstrap.ReturnToJobSelector();
                var show = GUILayout.Toggle(appBootstrap.IsFixtureOverlayVisible, " Show Fixture", bh);
                if (show != appBootstrap.IsFixtureOverlayVisible) appBootstrap.SetFixtureOverlayVisible(show);
                GUILayout.EndHorizontal();
            }
        }

        // Always-on transient safety overlay, independent of the ControlDrawer.
        // When an active job loses a solid tracking lock the hologram is hidden
        // (option A fail-safe), so the worker must be told WHY it vanished and
        // what to do. This is the only thing this component draws outside
        // DrawContent(); it is a no-op whenever the hint is not active.
        private void OnGUI()
        {
            if (!_showReacquireHint) return;

            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(ImguiTheme.FontSize * 1.1f),
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                    richText = true,
                };
                // Bright warm colour reads clearly on the additive waveguide
                // (dark pixels are invisible there, so we rely on luminance).
                _hintStyle.normal.textColor = new Color(1f, 0.85f, 0.15f);
            }

            float w = Screen.width * 0.7f;
            float h = ImguiTheme.ControlHeight * 1.8f;
            var rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.08f, w, h);
            GUI.Label(rect, "<b>Tracking lost - aim the glasses at the fixture</b>", _hintStyle);
        }
    }
}
