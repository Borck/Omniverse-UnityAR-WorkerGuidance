using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;
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
        private readonly string _desiredJobId;

        private System.Threading.SynchronizationContext _mainThreadContext;

        private GrpcChannel _channel;
        private GuidanceSessionService.GuidanceSessionServiceClient _client;
        private AsyncDuplexStreamingCall<ClientMessage, ServerMessage> _call;
        private CancellationTokenSource _readCancellation;
        private string _sessionId = string.Empty;

        public event Action Connected;
        public event Action<StepActivationDto> StepActivated;
        public event Action<string> Faulted;
        public event Action WorkflowCompleted;

        public bool IsConnected { get; private set; }

        public GrpcSessionTransport(string target, string deviceId, string appVersion, string desiredJobId = "")
        {
            _target = target;
            _deviceId = deviceId;
            _appVersion = appVersion;
            _desiredJobId = desiredJobId ?? string.Empty;
            _mainThreadContext = System.Threading.SynchronizationContext.Current;
        }

        public void Connect()
        {
            if (_call != null)
            {
                return;
            }

            try
            {
                // YetAnotherHttpHandler is a Rust-based HTTP/2 client that works on
                // Unity Android IL2CPP where SocketsHttpHandler is unavailable.
                // Http2Only = true forces h2c (cleartext HTTP/2) prior-knowledge for the
                // Grpc.Net.Client transport, matching the Python grpcio server.
                var handler = new YetAnotherHttpHandler { Http2Only = true };
                var httpClient = new System.Net.Http.HttpClient(handler);
                _channel = GrpcChannel.ForAddress($"http://{_target}", new GrpcChannelOptions
                {
                    HttpClient = httpClient,
                    DisposeHttpClient = true,
                });
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

        public void SendUserAction(string jobId, string stepId, UserActionType action)
        {
            if (_call == null)
            {
                Faulted?.Invoke("Cannot send user action while disconnected");
                return;
            }

            _ = WriteUserActionAsync(jobId, stepId, action);
        }

        private string BuildCapabilitiesString()
        {
            if (string.IsNullOrEmpty(_desiredJobId))
            {
                return "unity-ar";
            }
            return $"unity-ar,job={_desiredJobId}";
        }

        private async Task WriteHelloAsync()
        {
            var myCall = _call;
            if (myCall == null) return;
            try
            {
                await myCall.RequestStream.WriteAsync(
                    new ClientMessage
                    {
                        Hello = new HelloRequest
                        {
                            DeviceId = _deviceId,
                            AppVersion = _appVersion,
                            Capabilities = BuildCapabilitiesString()
                        }
                    }
                );
            }
            catch (Exception ex)
            {
                var msg = $"gRPC hello failed: {ex.Message}";
                if (_mainThreadContext != null)
                    _mainThreadContext.Post(_ => Faulted?.Invoke(msg), null);
                else
                    Faulted?.Invoke(msg);
                CleanupConnection();
            }
        }

        private async Task WriteHeartbeatAsync(long clientTimeUnixMs)
        {
            var myCall = _call;
            if (myCall == null) return;
            try
            {
                await myCall.RequestStream.WriteAsync(
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
                var msg = $"gRPC heartbeat failed: {ex.Message}";
                if (_mainThreadContext != null)
                    _mainThreadContext.Post(_ => Faulted?.Invoke(msg), null);
                else
                    Faulted?.Invoke(msg);
                CleanupConnection();
            }
        }

        private async Task WriteStepCompletedAsync(string jobId, string stepId, long completedAtUnixMs)
        {
            var myCall = _call;
            if (myCall == null) return;
            try
            {
                await myCall.RequestStream.WriteAsync(
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
                var msg = $"gRPC step_completed failed: {ex.Message}";
                if (_mainThreadContext != null)
                    _mainThreadContext.Post(_ => Faulted?.Invoke(msg), null);
                else
                    Faulted?.Invoke(msg);
                CleanupConnection();
            }
        }

        private async Task WriteUserActionAsync(string jobId, string stepId, UserActionType action)
        {
            var myCall = _call;
            if (myCall == null) return;
            try
            {
                await myCall.RequestStream.WriteAsync(
                    new ClientMessage
                    {
                        UserAction = new UserAction
                        {
                            JobId = jobId,
                            StepId = stepId,
                            Action = action,
                        }
                    }
                );
            }
            catch (Exception ex)
            {
                var msg = $"gRPC user_action failed: {ex.Message}";
                if (_mainThreadContext != null)
                    _mainThreadContext.Post(_ => Faulted?.Invoke(msg), null);
                else
                    Faulted?.Invoke(msg);
                CleanupConnection();
            }
        }

        private async Task ReadLoopAsync()
        {
            // Capture local references so the finally guard can tell whether a concurrent
            // TryReconnect() has already swapped in a new call before this loop exits.
            var myCall = _call;
            var myCancellation = _readCancellation;

            try
            {
                while (myCall != null && await myCall.ResponseStream.MoveNext(myCancellation.Token))
                {
                    var message = myCall.ResponseStream.Current;
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

                                if (_mainThreadContext != null)
                                    _mainThreadContext.Post(_ => Connected?.Invoke(), null);
                                else
                                    Connected?.Invoke();
                            }
                            break;

                        case ServerMessage.PayloadOneofCase.StepActivated:
                            if (_mainThreadContext != null)
                            {
                                _mainThreadContext.Post(_ => StepActivated?.Invoke(
                                    new StepActivationDto(
                                        message.StepActivated.JobId,
                                        message.StepActivated.StepId,
                                        message.StepActivated.PartId,
                                        message.StepActivated.DisplayName,
                                        message.StepActivated.InstructionsShort,
                                        message.StepActivated.AssetVersion,
                                        message.StepActivated.TargetId,
                                        message.StepActivated.TargetVersion,
                                        message.StepActivated.AnchorType
                                    )
                                ), null);
                            }
                            break;

                        case ServerMessage.PayloadOneofCase.Fault:
                            if (_mainThreadContext != null)
                                _mainThreadContext.Post(_ => Faulted?.Invoke($"gRPC server fault: {message.Fault.Code} {message.Fault.Message}"), null);
                            break;

                        case ServerMessage.PayloadOneofCase.Ping:
                        case ServerMessage.PayloadOneofCase.AssignJob:
                        case ServerMessage.PayloadOneofCase.CancelStep:
                        case ServerMessage.PayloadOneofCase.None:
                        default:
                            break;
                    }
                }

                // Stream ended cleanly by the server (last step completed, no further steps).
                if (!myCancellation.IsCancellationRequested && IsConnected)
                {
                    if (_mainThreadContext != null)
                        _mainThreadContext.Post(_ => WorkflowCompleted?.Invoke(), null);
                    else
                        WorkflowCompleted?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                // Normal — raised when myCancellation is cancelled during Disconnect().
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                // Normal — Grpc.Net.Client wraps OperationCanceledException as
                // RpcException(Cancelled) on client-side teardown. Not a server error.
            }
            catch (Exception ex)
            {
                var msg = $"gRPC read loop failed: {ex.Message}";
                if (_mainThreadContext != null)
                    _mainThreadContext.Post(_ => Faulted?.Invoke(msg), null);
                else
                    Faulted?.Invoke(msg);
            }
            finally
            {
                // Only clean up if WE are still the active call. A concurrent
                // TryReconnect() calls Disconnect() then Connect() on the main thread,
                // which can replace _call before this background finally block runs.
                // Cleaning up in that case would destroy the brand-new connection.
                if (_call == myCall)
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
                    _channel.Dispose();
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
