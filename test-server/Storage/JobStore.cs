using System.Collections.Concurrent;

namespace GuidanceAdminServer.Storage;

/// <summary>
/// In-memory store for assembly jobs and their steps.
/// Provides a signalling mechanism so connected gRPC streams are notified
/// when the operator submits a new job via the web UI.
/// </summary>
public sealed class JobStore
{
    private readonly ConcurrentDictionary<string, JobRecord> _jobs = new();
    private readonly List<TaskCompletionSource<JobRecord>> _waiters = new();
    private readonly object _lock = new();

    /// <summary>Registers or replaces a job definition.</summary>
    public void Upsert(JobRecord job)
    {
        _jobs[job.JobId] = job;
    }

    /// <summary>Returns the job record, or null if not found.</summary>
    public JobRecord? Get(string jobId)
    {
        _jobs.TryGetValue(jobId, out var record);
        return record;
    }

    /// <summary>Returns all registered jobs.</summary>
    public IReadOnlyList<JobRecord> All() => _jobs.Values.ToList();

    /// <summary>
    /// Activates the first step of the given job and notifies all waiting gRPC streams.
    /// </summary>
    public void SetActiveJob(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return;
        }

        List<TaskCompletionSource<JobRecord>> toNotify;
        lock (_lock)
        {
            toNotify = new List<TaskCompletionSource<JobRecord>>(_waiters);
            _waiters.Clear();
        }

        foreach (var waiter in toNotify)
        {
            waiter.TrySetResult(job);
        }
    }

    /// <summary>
    /// Returns a task that completes when the next job is activated.
    /// Used by gRPC streams to block until the operator submits a job.
    /// </summary>
    public Task<JobRecord> WaitForNextJobAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<JobRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetCanceled(ct));

        lock (_lock)
        {
            _waiters.Add(tcs);
        }

        return tcs.Task;
    }

    /// <summary>Finds the next step after the given completed step, or null if finished.</summary>
    public StepRecord? NextStep(string jobId, string completedStepId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return null;
        }

        for (var i = 0; i < job.Steps.Count - 1; i++)
        {
            if (job.Steps[i].StepId == completedStepId)
            {
                return job.Steps[i + 1];
            }
        }

        return null;
    }
}

public sealed record JobRecord(
    string JobId,
    string WorkflowVersion,
    IReadOnlyList<StepRecord> Steps);

public sealed record StepRecord(
    string StepId,
    string PartId,
    string DisplayName,
    string InstructionsShort,
    IReadOnlyList<string> SafetyNotes,
    string AssetVersion,
    string TargetId,
    string TargetVersion,
    string AnchorType,
    string AnimationName,
    string GlbFileName,
    string TargetFileName);
