using UnityEngine;
using System;

namespace Guidance.Runtime
{
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private bool useNativeGrpcTransport = true;
        [SerializeField] private string grpcTarget = "localhost:50051";
        [SerializeField] private string httpBridgeBaseUrl = "http://localhost:8080";
        [SerializeField] private float heartbeatIntervalSeconds = 5f;
        [SerializeField] private float reconnectIntervalSeconds = 3f;

        private SessionClient _sessionClient;
        private StepCoordinator _stepCoordinator;
        private float _nextHeartbeatAt;
        private float _nextReconnectAt;

        private void Awake()
        {
            ISessionTransport transport = useNativeGrpcTransport
                ? new GrpcSessionTransport(
                    target: grpcTarget,
                    deviceId: SystemInfo.deviceUniqueIdentifier,
                    appVersion: Application.version
                )
                : new HttpBridgeSessionTransport(
                    baseUrl: httpBridgeBaseUrl,
                    deviceId: SystemInfo.deviceUniqueIdentifier,
                    appVersion: Application.version
                );

            _sessionClient = new SessionClient(supportsDraco: true, transport: transport);
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
            _nextHeartbeatAt = Time.time + heartbeatIntervalSeconds;
            _nextReconnectAt = Time.time + reconnectIntervalSeconds;
        }

        private void Update()
        {
            if (_sessionClient == null)
            {
                return;
            }

            var now = Time.time;
            if (_sessionClient.ConnectionState == SessionConnectionState.Connected)
            {
                if (now >= _nextHeartbeatAt)
                {
                    var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    _sessionClient.SendHeartbeat(unixMs);
                    _nextHeartbeatAt = now + heartbeatIntervalSeconds;
                }
                return;
            }

            if (now >= _nextReconnectAt)
            {
                _sessionClient.TryReconnect();
                _nextReconnectAt = now + reconnectIntervalSeconds;
            }
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
