using UnityEngine;
using System;

namespace Guidance.Runtime
{
    public sealed class SessionClient
    {
        public bool SupportsDraco { get; }
        public SessionConnectionState ConnectionState { get; private set; } = SessionConnectionState.Disconnected;

        private readonly AssetStreamAssembler _assembler;
        private readonly ISessionTransport _transport;

        public event Action<StepActivationDto> StepActivated;
        public event Action<SessionConnectionState> ConnectionStateChanged;

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

        public void Initialize()
        {
            var compressionMode = SupportsDraco ? "draco-enabled" : "draco-disabled";
            _transport.Connected += OnTransportConnected;
            _transport.StepActivated += OnTransportStepActivated;
            _transport.Faulted += OnTransportFaulted;
            Debug.Log($"[SessionClient] Initialized ({compressionMode}, transport={_transport.GetType().Name}).");
        }

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

        public void TryReconnect()
        {
            if (ConnectionState == SessionConnectionState.Connected)
            {
                return;
            }

            Debug.Log("[SessionClient] Attempting reconnect.");
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
