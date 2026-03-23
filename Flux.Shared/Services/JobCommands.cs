using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flux.Core.Services;
using Flux.Shared.Abstractions;
using RuriLib.Models.Jobs;

namespace Flux.Shared.Services;

public class JobCommands : IJobCommands, IDisposable
{
    private readonly JobManagerService _jobManager;
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _runningStarts = new();

    public JobCommands(JobManagerService jobManager)
    {
        _jobManager = jobManager;
    }

    public async Task StartAsync(int jobId, IReadOnlyDictionary<string, string>? customInputs = null, CancellationToken cancellationToken = default)
    {
        var job = FindMultiRunJob(jobId);
        ApplyCustomInputs(job, customInputs);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_runningStarts.TryAdd(job.Id, cts))
        {
            cts.Dispose();
            throw new InvalidOperationException($"Job {job.Id} is already being started");
        }

        try
        {
            await job.Start(cts.Token).ConfigureAwait(false);
        }
        finally
        {
            if (_runningStarts.TryRemove(job.Id, out var ownedCts))
            {
                ownedCts.Dispose();
            }
        }
    }

    public Task StopAsync(int jobId, CancellationToken cancellationToken = default)
        => FindMultiRunJob(jobId).Stop();

    public async Task AbortAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = FindMultiRunJob(jobId);

        if (job.Status is JobStatus.Starting or JobStatus.Waiting
            && _runningStarts.TryGetValue(job.Id, out var cts))
        {
            await cts.CancelAsync().ConfigureAwait(false);
            return;
        }

        await job.Abort().ConfigureAwait(false);
    }

    public Task PauseAsync(int jobId, CancellationToken cancellationToken = default)
        => FindMultiRunJob(jobId).Pause();

    public Task ResumeAsync(int jobId, CancellationToken cancellationToken = default)
        => FindMultiRunJob(jobId).Resume();

    public async Task ChangeBotsAsync(int jobId, int bots, CancellationToken cancellationToken = default)
    {
        var job = FindMultiRunJob(jobId);
        var normalizedBots = Math.Max(1, bots);
        await job.ChangeBots(normalizedBots).ConfigureAwait(false);
        job.Bots = normalizedBots;
    }

    public Task SkipWaitAsync(int jobId, CancellationToken cancellationToken = default)
    {
        FindMultiRunJob(jobId).SkipWait();
        return Task.CompletedTask;
    }

    public Task ResetSkipAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = FindMultiRunJob(jobId);
        if (job.Status is not JobStatus.Idle)
        {
            return Task.CompletedTask;
        }

        job.Skip = 0;
        job.DataPool.Reload();
        return Task.CompletedTask;
    }

    private MultiRunJob FindMultiRunJob(int jobId)
        => _jobManager.Jobs.OfType<MultiRunJob>().FirstOrDefault(job => job.Id == jobId)
            ?? throw new InvalidOperationException($"Multi-run job {jobId} is not loaded");

    private static void ApplyCustomInputs(MultiRunJob job, IReadOnlyDictionary<string, string>? customInputs)
    {
        job.CustomInputsAnswers.Clear();
        if (customInputs is null)
        {
            return;
        }

        foreach (var answer in customInputs)
        {
            job.CustomInputsAnswers[answer.Key] = answer.Value;
        }
    }

    public void Dispose()
    {
        foreach (var cts in _runningStarts.Values)
        {
            cts.Dispose();
        }

        _runningStarts.Clear();
    }
}
