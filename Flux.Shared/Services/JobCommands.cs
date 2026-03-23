using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Flux.Shared.Abstractions;
using RuriLib.Models.Jobs;

namespace Flux.Shared.Services;

public class JobCommands : IJobCommands, IDisposable
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _runningStarts = new();

    public async Task StartAsync(MultiRunJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var cts = new CancellationTokenSource();
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

    public Task StopAsync(MultiRunJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Stop();
    }

    public async Task AbortAsync(MultiRunJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Status is JobStatus.Starting or JobStatus.Waiting
            && _runningStarts.TryGetValue(job.Id, out var cts))
        {
            await cts.CancelAsync().ConfigureAwait(false);
            return;
        }

        await job.Abort().ConfigureAwait(false);
    }

    public Task PauseAsync(MultiRunJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Pause();
    }

    public Task ResumeAsync(MultiRunJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Resume();
    }

    public async Task ChangeBotsAsync(MultiRunJob job, int bots)
    {
        ArgumentNullException.ThrowIfNull(job);

        var normalizedBots = Math.Max(1, bots);
        await job.ChangeBots(normalizedBots).ConfigureAwait(false);
        job.Bots = normalizedBots;
    }

    public void SkipWait(MultiRunJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.SkipWait();
    }

    public void ResetSkip(MultiRunJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Status is not JobStatus.Idle)
        {
            return;
        }

        job.Skip = 0;
        job.DataPool.Reload();
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
