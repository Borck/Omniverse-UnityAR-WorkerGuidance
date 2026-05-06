using UnityEngine;
using System;
using Guidance.V1;

namespace Guidance.Runtime
{
    /// <summary>
    /// Client-side session facade for connect, heartbeat, progression, and asset stream assembly.
    /// </summary>
    public sealed class SessionClient
    {
        public bool SupportsDraco { get; }
        public SessionConnectionState ConnectionState { get; private set; } = SessionConnectionState.Disconnected;

        private readonly AssetStreamAssembler _assembler;
        private readonly ISessionTransport _transport;

        public event Action<StepActivationDto> StepActivated;
        public event Action<SessionConnectionState> ConnectionStateChanged;
        public event Action WorkflowCompleted;

        public SessionClient(bool supportsDraco)
            : this(
                supportsDraco,
                new GrpcSessionTransport(
                    target: "localhost:50051",
                    deviceId: SystemInfo.deviceUniqueIdentifier,
                    appVersion: Application.version
                )
            )
        {
        }

        public SessionClient(bool supportsDraco, string target, string deviceId, string appVersion)
            : this(
                supportsDraco,
                new GrpcSessionTransport(
                    target: target,
                    deviceId: deviceId,
                    appVersion: appVersion
                )
            )
        {
        }

        public SessionClient(bool supportsDraco, ISessionTransport transport)
        {
            SupportsDraco = supportsDraco;
            _assembler = new AssetStreamAssembler(SupportsDraco);
            _transport = transport;
        }

        /// <summary>
        /// Subscribes to transport events and prepares the session client for use.
        /// </summary>
        public void Initialize()
        {
            var compressionMode = SupportsDraco ? "draco-enabled" : "draco-disabled";
            _transport.Connected += OnTransportConnected;
            _transport.StepActivated += OnTransportStepActivated;
            _transport.Faulted += OnTransportFaulted;
            _transport.WorkflowCompleted += OnTransportWorkflowCompleted;
            Debug.Log($"[SessionClient] Initialized ({compressionMode}, transport={_transport.GetType().Name}).");
        }

        /// <summary>
        /// Initiates a transport connection.
        /// </summary>
        public void Connect()
        {
            _transport.Connect();
        }

        public void Disconnect()
        {
            _transport.Disconnect();
            SetConnectionState(SessionConnectionState.Disconnected);
        }

        public void SendHeartbeat(long clientTimeUnixMs)
        {
            _transport.SendHeartbeat(clientTimeUnixMs);
        }

        /// <summary>
        /// Sends step completion to the active transport.
        /// </summary>
        public void SendStepCompleted(string jobId, string stepId, long completedAtUnixMs)
        {
            _transport.SendStepCompleted(jobId, stepId, completedAtUnixMs);
        }

        public void SendUserAction(string jobId, string stepId, UserActionType action)
        {
            _transport.SendUserAction(jobId, stepId, action);
        }

        public void TryReconnect()
        {
            if (ConnectionState == SessionConnectionState.Connected)
            {
                return;
            }

            Debug.Log("[SessionClient] Attempting reconnect.");
            // Tear down any stale call before reconnecting. Without this, a previous
            // Connect() that left _call non-null (e.g. an HTTP/2 handshake hanging
            // on a flaky link, or a faulted task whose CleanupConnection never ran)
            // causes the next _transport.Connect() to no-op via its
            // `if (_call != null) return;` guard — and the app sits forever logging
            // "Attempting reconnect" without ever actually opening a new socket.
            _transport.Disconnect();
            _transport.Connect();
        }

        public void HandleAssetChunk(AssetChunkDto chunk, string outputFilePath)
        {
            var payload = _assembler.AppendChunk(chunk);
            if (payload.Length == 0)
            {
                return;
            }

            _assembler.SaveToFile(payload, outputFilePath);
        }

        private void OnTransportConnected()
        {
            SetConnectionState(SessionConnectionState.Connected);
        }

        private void OnTransportStepActivated(StepActivationDto activation)
        {
            StepActivated?.Invoke(activation);
        }

        private void OnTransportFaulted(string error)
        {
            Debug.LogWarning($"[SessionClient] Transport fault: {error}");
            SetConnectionState(SessionConnectionState.Faulted);
        }

        private void OnTransportWorkflowCompleted()
        {
            Debug.Log("[SessionClient] Workflow completed — no further steps from server.");
            WorkflowCompleted?.Invoke();
        }

        private void SetConnectionState(SessionConnectionState state)
        {
            if (ConnectionState == state)
            {
                return;
            }

            ConnectionState = state;
            Debug.Log($"[SessionClient] Connection state changed to {state}");
            ConnectionStateChanged?.Invoke(state);
        }
    }
}
