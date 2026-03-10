using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Guidance.Runtime
{
    // Android-safe transport path via HTTP bridge/gRPC-Web gateway.
    public sealed class HttpBridgeSessionTransport : ISessionTransport
    {
        private readonly string _baseUrl;
        private readonly string _deviceId;
        private readonly string _appVersion;
        private string _sessionId = string.Empty;

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

            var payload = new ClientEnvelope
            {
                hello = new HelloRequestPayload
                {
                    device_id = _deviceId,
                    app_version = _appVersion,
                    capabilities = "unity-ar"
                }
            };

            var json = JsonUtility.ToJson(payload);
            var url = _baseUrl + "/session/connect";

            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");

            var asyncOp = request.SendWebRequest();
            asyncOp.completed += _ =>
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Faulted?.Invoke($"Handshake failed: {request.error}");
                    request.Dispose();
                    return;
                }

                ProcessConnectResponse(request.downloadHandler.text);
                request.Dispose();
            };
        }

        public void Disconnect()
        {
            if (!IsConnected)
            {
                return;
            }

            IsConnected = false;
            _sessionId = string.Empty;
            Debug.Log("[HttpBridgeSessionTransport] Disconnected");
        }

        public void SendHeartbeat(long clientTimeUnixMs)
        {
            if (!IsConnected)
            {
                Faulted?.Invoke("Cannot send heartbeat while disconnected");
                return;
            }

            var payload = new ClientEnvelope
            {
                heartbeat = new HeartbeatPayload
                {
                    session_id = _sessionId,
                    client_time_unix_ms = clientTimeUnixMs
                }
            };

            var json = JsonUtility.ToJson(payload);
            var url = _baseUrl + "/session/heartbeat";

            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");

            var asyncOp = request.SendWebRequest();
            asyncOp.completed += _ =>
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Faulted?.Invoke($"Heartbeat failed: {request.error}");
                    request.Dispose();
                    return;
                }

                ProcessHeartbeatResponse(request.downloadHandler.text, clientTimeUnixMs);
                request.Dispose();
            };
        }

        private void ProcessConnectResponse(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                Faulted?.Invoke("Handshake response body is empty");
                return;
            }

            var message = JsonUtility.FromJson<ServerEnvelope>(responseJson);
            if (message == null)
            {
                Faulted?.Invoke("Handshake response could not be parsed");
                return;
            }

            if (message.fault != null && !string.IsNullOrEmpty(message.fault.message))
            {
                Faulted?.Invoke($"Server fault: {message.fault.code} {message.fault.message}");
                return;
            }

            if (message.hello_response == null || string.IsNullOrEmpty(message.hello_response.session_id))
            {
                Faulted?.Invoke("Handshake response missing hello_response.session_id");
                return;
            }

            _sessionId = message.hello_response.session_id;
            IsConnected = true;
            Debug.Log($"[HttpBridgeSessionTransport] Connected baseUrl={_baseUrl} session={_sessionId}");
            Connected?.Invoke();

            if (message.step_activated != null)
            {
                StepActivated?.Invoke(
                    new StepActivationDto(
                        message.step_activated.job_id,
                        message.step_activated.step_id,
                        message.step_activated.part_id,
                        message.step_activated.display_name
                    )
                );
            }
        }

        private void ProcessHeartbeatResponse(string responseJson, long clientTimeUnixMs)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                Debug.Log($"[HttpBridgeSessionTransport] Heartbeat sent (no body) at {clientTimeUnixMs}");
                return;
            }

            var message = JsonUtility.FromJson<ServerEnvelope>(responseJson);
            if (message == null)
            {
                Faulted?.Invoke("Heartbeat response could not be parsed");
                return;
            }

            if (message.fault != null && !string.IsNullOrEmpty(message.fault.message))
            {
                Faulted?.Invoke($"Server fault: {message.fault.code} {message.fault.message}");
                return;
            }

            if (message.ping != null)
            {
                Debug.Log($"[HttpBridgeSessionTransport] Heartbeat ack nonce={message.ping.nonce}");
            }
            else
            {
                Debug.Log($"[HttpBridgeSessionTransport] Heartbeat sent at {clientTimeUnixMs}");
            }
        }

        [Serializable]
        private sealed class ClientEnvelope
        {
            public HelloRequestPayload hello;
            public HeartbeatPayload heartbeat;
        }

        [Serializable]
        private sealed class HelloRequestPayload
        {
            public string device_id;
            public string app_version;
            public string capabilities;
        }

        [Serializable]
        private sealed class HeartbeatPayload
        {
            public string session_id;
            public long client_time_unix_ms;
        }

        [Serializable]
        private sealed class ServerEnvelope
        {
            public HelloResponsePayload hello_response;
            public StepActivatedPayload step_activated;
            public PingPayload ping;
            public FaultPayload fault;
        }

        [Serializable]
        private sealed class HelloResponsePayload
        {
            public string session_id;
            public string protocol_version;
            public long server_time_unix_ms;
        }

        [Serializable]
        private sealed class StepActivatedPayload
        {
            public string job_id;
            public string step_id;
            public string part_id;
            public string display_name;
        }

        [Serializable]
        private sealed class PingPayload
        {
            public string nonce;
        }

        [Serializable]
        private sealed class FaultPayload
        {
            public string code;
            public string message;
            public string correlation_id;
            public bool recoverable;
        }
    }
}
