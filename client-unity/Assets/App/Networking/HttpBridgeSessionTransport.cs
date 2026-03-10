using System;
using UnityEngine;

namespace Guidance.Runtime
{
    // Android-safe default transport placeholder: HTTP bridge/gRPC-Web gateway path.
    public sealed class HttpBridgeSessionTransport : ISessionTransport
    {
        private readonly string _baseUrl;
        private readonly string _deviceId;
        private readonly string _appVersion;

        public event Action Connected;
        public event Action<StepActivationDto> StepActivated;
        public event Action<string> Faulted;

        public bool IsConnected { get; private set; }

        public HttpBridgeSessionTransport(string baseUrl, string deviceId, string appVersion)
        {
            _baseUrl = baseUrl;
            _deviceId = deviceId;
            _appVersion = appVersion;
        }

        public void Connect()
        {
            if (IsConnected)
            {
                return;
            }

            // Placeholder connect for vertical-slice wiring. This is where HTTP bridge/gRPC-Web
            // session handshake should be called once transport endpoint is available.
            IsConnected = true;
            Debug.Log($"[HttpBridgeSessionTransport] Connected baseUrl={_baseUrl} device={_deviceId} app={_appVersion}");
            Connected?.Invoke();

            // Emit one mock step so coordinator integration can be validated before full transport wiring.
            StepActivated?.Invoke(
                new StepActivationDto(
                    jobId: "job-mock-001",
                    stepId: "17",
                    partId: "Bracket_12",
                    displayName: "Install bracket"
                )
            );
        }

        public void Disconnect()
        {
            if (!IsConnected)
            {
                return;
            }

            IsConnected = false;
            Debug.Log("[HttpBridgeSessionTransport] Disconnected");
        }

        public void SendHeartbeat(long clientTimeUnixMs)
        {
            if (!IsConnected)
            {
                Faulted?.Invoke("Cannot send heartbeat while disconnected");
                return;
            }

            Debug.Log($"[HttpBridgeSessionTransport] Heartbeat {clientTimeUnixMs}");
        }
    }
}
