using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Guidance.V1;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Native gRPC duplex-stream transport used as the default runtime path.
    /// </summary>
    public sealed class GrpcSessionTransport : ISessionTransport
    {
        private readonly string _target;
        private readonly string _deviceId;
        private readonly string _appVersion;

        private Channel _channel;
        private GuidanceSessionService.GuidanceSessionServiceClient _client;
        private AsyncDuplexStreamingCall<ClientMessage, ServerMessage> _call;
        private CancellationTokenSource _readCancellation;
        private string _sessionId = string.Empty;

        public event Action Connected;
        public event Action<StepActivationDto> StepActivated;
        public event Action<string> Faulted;

        public bool IsConnected { get; private set; }

        public GrpcSessionTransport(string target, string deviceId, string appVersion)
        {
            _target = target;
            _deviceId = deviceId;
            _appVersion = appVersion;
        }

        public void Connect()
        {
            if (_call != null)
            {
                return;
            }

            try
            {
                _channel = new Channel(_target, ChannelCredentials.Insecure);
                _client = new GuidanceSessionService.GuidanceSessionServiceClient(_channel);
                _call = _client.Connect();
                _readCancellation = new CancellationTokenSource();

                _ = Task.Run(ReadLoopAsync);
                _ = WriteHelloAsync();
            }
            catch (Exception ex)
            {
                Faulted?.Invoke($"gRPC connect failed: {ex.Message}");
                CleanupConnection();
            }
        }

        public void Disconnect()
        {
            CleanupConnection();
        }

        public void SendHeartbeat(long clientTimeUnixMs)
        {
            if (_call == null)
            {
                Faulted?.Invoke("Cannot send heartbeat while disconnected");
                return;
            }

            _ = WriteHeartbeatAsync(clientTimeUnixMs);
        }

        public void SendStepCompleted(string jobId, string stepId, long completedAtUnixMs)
        {
            if (_call == null)
            {
                Faulted?.Invoke("Cannot send step completion while disconnected");
                return;
            }

            _ = WriteStepCompletedAsync(jobId, stepId, completedAtUnixMs);
        }

        private async Task WriteHelloAsync()
        {
            try
            {
                await _call.RequestStream.WriteAsync(
                    new ClientMessage
                    {
                        Hello = new HelloRequest
                        {
                            DeviceId = _deviceId,
                            AppVersion = _appVersion,
                            Capabilities = "unity-ar"
                        }
                    }
                );
            }
            catch (Exception ex)
            {
                Faulted?.Invoke($"gRPC hello failed: {ex.Message}");
                CleanupConnection();
            }
        }

        private async Task WriteHeartbeatAsync(long clientTimeUnixMs)
        {
            try
            {
                await _call.RequestStream.WriteAsync(
                    new ClientMessage
                    {
                        Heartbeat = new Heartbeat
                        {
                            SessionId = _sessionId,
                            ClientTimeUnixMs = clientTimeUnixMs
                        }
                    }
                );
            }
            catch (Exception ex)
            {
                Faulted?.Invoke($"gRPC heartbeat failed: {ex.Message}");
                CleanupConnection();
            }
        }

        private async Task WriteStepCompletedAsync(string jobId, string stepId, long completedAtUnixMs)
        {
            try
            {
                await _call.RequestStream.WriteAsync(
                    new ClientMessage
                    {
                        StepCompleted = new StepCompleted
                        {
                            JobId = jobId,
                            StepId = stepId,
                            CompletedAtUnixMs = completedAtUnixMs,
                        }
                    }
                );
            }
            catch (Exception ex)
            {
                Faulted?.Invoke($"gRPC step_completed failed: {ex.Message}");
                CleanupConnection();
            }
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (_call != null && await _call.ResponseStream.MoveNext(_readCancellation.Token))
                {
                    var message = _call.ResponseStream.Current;
                    if (message == null)
                    {
                        continue;
                    }

                    switch (message.PayloadCase)
                    {
                        case ServerMessage.PayloadOneofCase.HelloResponse:
                            _sessionId = message.HelloResponse.SessionId;
                            if (!IsConnected)
                            {
                                IsConnected = true;
                                Debug.Log($"[GrpcSessionTransport] Connected target={_target} session={_sessionId}");
                                Connected?.Invoke();
                            }
                            break;

                        case ServerMessage.PayloadOneofCase.StepActivated:
                            StepActivated?.Invoke(
                                new StepActivationDto(
                                    message.StepActivated.JobId,
                                    message.StepActivated.StepId,
                                    message.StepActivated.PartId,
                                    message.StepActivated.DisplayName,
                                    message.StepActivated.AssetVersion,
                                    message.StepActivated.TargetId,
                                    message.StepActivated.TargetVersion
                                )
                            );
                            break;

                        case ServerMessage.PayloadOneofCase.Fault:
                            Faulted?.Invoke($"gRPC server fault: {message.Fault.Code} {message.Fault.Message}");
                            break;

                        case ServerMessage.PayloadOneofCase.Ping:
                        case ServerMessage.PayloadOneofCase.AssignJob:
                        case ServerMessage.PayloadOneofCase.CancelStep:
                        case ServerMessage.PayloadOneofCase.None:
                        default:
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // normal during disconnect/reconnect
            }
            catch (Exception ex)
            {
                Faulted?.Invoke($"gRPC read loop failed: {ex.Message}");
            }
            finally
            {
                if (_call != null)
                {
                    CleanupConnection();
                }
            }
        }

        private void CleanupConnection()
        {
            IsConnected = false;
            _sessionId = string.Empty;

            try
            {
                _readCancellation?.Cancel();
            }
            catch
            {
            }

            try
            {
                _call?.RequestStream.CompleteAsync();
            }
            catch
            {
            }

            _call?.Dispose();
            _call = null;
            _client = null;

            if (_channel != null)
            {
                try
                {
                    _channel.ShutdownAsync().GetAwaiter().GetResult();
                }
                catch
                {
                }
                _channel = null;
            }

            _readCancellation?.Dispose();
            _readCancellation = null;
        }
    }
}
