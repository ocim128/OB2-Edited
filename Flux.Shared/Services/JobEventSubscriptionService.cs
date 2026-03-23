using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;
using RuriLib.Models.Hits;
using RuriLib.Models.Jobs;
using RuriLib.Models.Jobs.Status;

namespace Flux.Shared.Services;

public class JobEventSubscriptionService(INotificationService notifications)
{
    private readonly Dictionary<int, MultiRunJob> _subscribedJobs = new();
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);

    public async Task SubscribeExistingAsync(IEnumerable<MultiRunJob> jobs)
    {
        foreach (var job in jobs)
        {
            await EnsureSubscribedAsync(job).ConfigureAwait(false);
        }
    }

    public async Task EnsureSubscribedAsync(MultiRunJob job)
    {
        await _subscriptionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_subscribedJobs.ContainsKey(job.Id))
            {
                return;
            }

            job.OnStatusChanged += HandleStatusChanged;
            job.OnHit += HandleHit;
            job.OnError += HandleError;
            job.OnCompleted += HandleCompleted;
            _subscribedJobs[job.Id] = job;
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    public async Task UnsubscribeAsync(MultiRunJob job)
    {
        await _subscriptionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_subscribedJobs.Remove(job.Id))
            {
                return;
            }

            job.OnStatusChanged -= HandleStatusChanged;
            job.OnHit -= HandleHit;
            job.OnError -= HandleError;
            job.OnCompleted -= HandleCompleted;
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    private void HandleStatusChanged(object sender, JobStatus status)
    {
        if (sender is not Job job)
        {
            return;
        }

        _ = PublishJobNotificationAsync(
            job,
            $"Job '{job.Name}' status changed to {status}",
            status is JobStatus.Running ? "success" : "info",
            CancellationToken.None);
    }

    private void HandleHit(object sender, Hit hit)
    {
        _ = notifications.PublishAsync(
            new NotificationDto("hits", $"[{hit.Type}] {hit.DataString}", "success", DateTime.UtcNow),
            CancellationToken.None);
    }

    private void HandleError(object sender, Exception exception)
    {
        if (sender is not Job job)
        {
            return;
        }

        _ = PublishJobNotificationAsync(
            job,
            $"Job '{job.Name}' error: {exception.Message}",
            "error",
            CancellationToken.None);
    }

    private void HandleCompleted(object sender, EventArgs e)
    {
        if (sender is not Job job)
        {
            return;
        }

        _ = PublishJobNotificationAsync(job, $"Job '{job.Name}' completed", "success", CancellationToken.None);
    }

    private Task PublishJobNotificationAsync(Job job, string message, string severity, CancellationToken cancellationToken)
        => notifications.PublishAsync(new NotificationDto("jobs", message, severity, DateTime.UtcNow), cancellationToken);
}
