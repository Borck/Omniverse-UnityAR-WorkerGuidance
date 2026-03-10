using UnityEngine;

namespace Guidance.Runtime
{
    public sealed class AppBootstrap : MonoBehaviour
    {
        private SessionClient _sessionClient;
        private StepCoordinator _stepCoordinator;

        private void Awake()
        {
            _sessionClient = new SessionClient(supportsDraco: true);
            _stepCoordinator = new StepCoordinator();
            _stepCoordinator.StateChanged += OnStepStateChanged;
            _sessionClient.StepActivated += OnSessionStepActivated;
            _sessionClient.ConnectionStateChanged += OnSessionConnectionStateChanged;
        }

        private void Start()
        {
            _sessionClient.Initialize();
            _stepCoordinator.Initialize();
            _sessionClient.Connect();
        }

        private static void OnStepStateChanged(
            StepCoordinatorState previous,
            StepCoordinatorState current,
            string reason)
        {
            Debug.Log($"[AppBootstrap] Step state changed {previous} -> {current} ({reason})");
        }

        private void OnSessionStepActivated(StepActivationDto activation)
        {
            Debug.Log($"[AppBootstrap] Step activated from session: {activation.JobId}/{activation.StepId}");
            _stepCoordinator.ActivateStep(activation.JobId, activation.StepId);
        }

        private static void OnSessionConnectionStateChanged(SessionConnectionState state)
        {
            Debug.Log($"[AppBootstrap] Session connection state: {state}");
        }
    }
}
