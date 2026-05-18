using System;
using Guidance.V1;

namespace Guidance.Runtime
{
    /// <summary>
    /// Transport abstraction used by session client for gRPC/HTTP interchangeability.
    /// </summary>
    public interface ISessionTransport
    {
        event Action Connected;
        event Action<StepActivationDto> StepActivated;
        event Action<string> Faulted;
        event Action WorkflowCompleted;

        bool IsConnected { get; }

        void Connect();
        void Disconnect();
        void SendHeartbeat(long clientTimeUnixMs);
        void SendStepCompleted(string jobId, string stepId, long completedAtUnixMs);
        void SendUserAction(string jobId, string stepId, UserActionType action);
    }
}
