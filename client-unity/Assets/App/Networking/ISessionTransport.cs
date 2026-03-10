using System;

namespace Guidance.Runtime
{
    public interface ISessionTransport
    {
        event Action Connected;
        event Action<StepActivationDto> StepActivated;
        event Action<string> Faulted;

        bool IsConnected { get; }

        void Connect();
        void Disconnect();
        void SendHeartbeat(long clientTimeUnixMs);
        void SendStepCompleted(string jobId, string stepId, long completedAtUnixMs);
    }
}
