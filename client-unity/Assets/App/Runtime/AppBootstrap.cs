using UnityEngine;

namespace Guidance.Runtime
{
    public sealed class AppBootstrap : MonoBehaviour
    {
        private SessionClient _sessionClient;
        private StepCoordinator _stepCoordinator;

        private void Awake()
        {
            _sessionClient = new SessionClient();
            _stepCoordinator = new StepCoordinator();
        }

        private void Start()
        {
            _sessionClient.Initialize();
            _stepCoordinator.Initialize();
        }
    }
}
