using Grpc.Core;
using Guidance.V1;
using GuidanceAdminServer.Storage;

namespace GuidanceAdminServer.Services;

/// <summary>
/// gRPC implementation of <see cref="GuidanceSessionService"/>.
/// Mirrors the Python server's Connect duplex logic:
///  - Sends <see cref="HelloResponse"/> immediately on Hello.
///  - Blocks until the operator submits a job via the web UI, then sends <see cref="StepActivated"/>.
///  - On <see cref="StepCompleted"/>, advances to the next step and sends <see cref="StepActivated"/>.
/// </summary>
public sealed class GuidanceSessionServiceImpl : GuidanceSessionService.GuidanceSessionServiceBase
{
    private readonly JobStore _jobStore;
    private readonly ILogger<GuidanceSessionServiceImpl> _logger;

    public GuidanceSessionServiceImpl(JobStore jobStore, ILogger<GuidanceSessionServiceImpl> logger)
    {
        _jobStore = jobStore;
        _logger = logger;
    }

    public override async Task Connect(
        IAsyncStreamReader<ClientMessage> requestStream,
        IServerStreamWriter<ServerMessage> responseStream,
        ServerCallContext context)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        string activeJobId = string.Empty;
        string activeStepId = string.Empty;

        _logger.LogInformation("Client connected session={Session}", sessionId);

        await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
        {
            switch (message.PayloadCase)
            {
                case ClientMessage.PayloadOneofCase.Hello:
                    _logger.LogInformation("Hello device={Device}", message.Hello.DeviceId);
                    await responseStream.WriteAsync(new ServerMessage
                    {
                        HelloResponse = new HelloResponse
                        {
                            SessionId = sessionId,
                            ProtocolVersion = "v1",
                            ServerTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        }
                    });

                    // Wait until the operator activates a job
                    var job = await _jobStore.WaitForNextJobAsync(context.CancellationToken);
                    if (job.Steps.Count > 0)
                    {
                        var first = job.Steps[0];
                        activeJobId = job.JobId;
                        activeStepId = first.StepId;
                        await responseStream.WriteAsync(BuildStepActivated(job.JobId, first));
                    }
                    break;

                case ClientMessage.PayloadOneofCase.StepCompleted:
                    var sc = message.StepCompleted;
                    _logger.LogInformation("StepCompleted job={Job} step={Step}", sc.JobId, sc.StepId);
                    var next = _jobStore.NextStep(sc.JobId, sc.StepId);
                    if (next != null)
                    {
                        activeStepId = next.StepId;
                        await responseStream.WriteAsync(BuildStepActivated(sc.JobId, next));
                    }
                    break;

                case ClientMessage.PayloadOneofCase.Heartbeat:
                    await responseStream.WriteAsync(new ServerMessage
                    {
                        Ping = new Ping { Nonce = $"hb-{message.Heartbeat.ClientTimeUnixMs}" }
                    });
                    break;

                default:
                    break;
            }
        }

        _logger.LogInformation("Client disconnected session={Session}", sessionId);
    }

    private static ServerMessage BuildStepActivated(string jobId, StepRecord step)
    {
        var activated = new StepActivated
        {
            JobId = jobId,
            StepId = step.StepId,
            PartId = step.PartId,
            DisplayName = step.DisplayName,
            InstructionsShort = step.InstructionsShort,
            AssetVersion = step.AssetVersion,
            TargetId = step.TargetId,
            TargetVersion = step.TargetVersion,
            AnchorType = step.AnchorType,
            AnimationName = step.AnimationName,
        };
        activated.SafetyNotes.AddRange(step.SafetyNotes);
        return new ServerMessage { StepActivated = activated };
    }
}
