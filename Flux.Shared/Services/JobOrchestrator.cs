using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Flux.Core.Entities;
using Flux.Core.Models.Data;
using Flux.Core.Models.Hits;
using Flux.Core.Models.Jobs;
using Flux.Core.Repositories;
using Flux.Core.Services;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;
using RuriLib.Models.Jobs;

namespace Flux.Shared.Services;

public class JobOrchestrator : IJobOrchestrator
{
    private readonly JobManagerService _jobManager;
    private readonly JobFactoryService _jobFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notifications;
    private readonly ILogger<JobOrchestrator> _logger;
    private readonly JobProjectionService _projections;
    private readonly JobEventSubscriptionService _subscriptions;
    private readonly JsonSerializerSettings _jsonSettings = new() { TypeNameHandling = TypeNameHandling.Auto };

    public JobOrchestrator(
        JobManagerService jobManager,
        JobFactoryService jobFactory,
        IServiceScopeFactory scopeFactory,
        INotificationService notifications,
        ILogger<JobOrchestrator> logger,
        JobProjectionService projections,
        JobEventSubscriptionService subscriptions)
    {
        _jobManager = jobManager;
        _jobFactory = jobFactory;
        _scopeFactory = scopeFactory;
        _notifications = notifications;
        _logger = logger;
        _projections = projections;
        _subscriptions = subscriptions;

        _subscriptions.SubscribeExistingAsync(_jobManager.Jobs.OfType<MultiRunJob>()).GetAwaiter().GetResult();
    }

    public async Task<JobDetailDto> CreateMultiRunJobAsync(JobCreateRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var wordlistRepo = scope.ServiceProvider.GetRequiredService<IWordlistRepository>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();

        var config = configService.GetConfigsList().FirstOrDefault(c => c.Id == request.ConfigId)
            ?? throw new InvalidOperationException($"Config '{request.ConfigId}' not found");

        var wordlist = await wordlistRepo.GetAsync(request.WordlistId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Wordlist '{request.WordlistId}' not found");

        var options = BuildOptions(request);
        options.DataPool = new WordlistDataPoolOptions { WordlistId = wordlist.Id };
        options.HitOutputs = new List<HitOutputOptions> { new DatabaseHitOutputOptions() };

        var wrapper = new JobOptionsWrapper { Options = options };
        var entity = new JobEntity
        {
            CreationDate = DateTime.UtcNow,
            JobType = JobType.MultiRun,
            JobOptions = JsonConvert.SerializeObject(wrapper, _jsonSettings)
        };

        await jobRepo.AddAsync(entity, cancellationToken).ConfigureAwait(false);

        if (await _jobFactory.FromOptionsAsync(entity.Id, ownerId: 0, options).ConfigureAwait(false) is not MultiRunJob job)
        {
            throw new InvalidOperationException("Expected MultiRunJob from factory");
        }

        job.Name = string.IsNullOrWhiteSpace(request.Name) ? $"{config.Metadata.Name} #{entity.Id}" : request.Name;
        _jobManager.AddJob(job);
        await _subscriptions.EnsureSubscribedAsync(job).ConfigureAwait(false);

        _logger.LogInformation("Created job {JobId} ({JobName})", entity.Id, job.Name);
        await _notifications.PublishAsync(
            new NotificationDto("jobs", $"Job '{job.Name}' created", "info", DateTime.UtcNow),
            cancellationToken).ConfigureAwait(false);

        if (request.AutoStart)
        {
            _ = Task.Run(async () => await StartJobInternalAsync(job, CancellationToken.None));
        }

        return await _projections.BuildDetailAsync(job, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Failed to build details for job {job.Id}");
    }

    public Task<JobDetailDto?> GetJobAsync(int jobId, CancellationToken cancellationToken = default)
        => _projections.BuildDetailAsync(FindJob(jobId), cancellationToken);

    public Task<IReadOnlyList<JobSummaryDto>> GetJobsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_projections.BuildSummaries(_jobManager.Jobs));

    public Task<JobQueueDto> GetQueueSnapshotAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_projections.BuildQueueSnapshot(_jobManager.Jobs));

    public async Task<IReadOnlyList<JobResultDto>> GetRecentResultsAsync(int jobId, int take = 200, CancellationToken cancellationToken = default)
        => await _projections.GetRecentResultsAsync(FindJob(jobId), take, cancellationToken).ConfigureAwait(false);

    public async Task<JobDetailDto?> StartJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is not MultiRunJob job)
        {
            return null;
        }

        await StartJobInternalAsync(job, cancellationToken).ConfigureAwait(false);
        return await _projections.BuildDetailAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobDetailDto?> PauseJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is not MultiRunJob job)
        {
            return null;
        }

        await job.Pause().ConfigureAwait(false);
        return await _projections.BuildDetailAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobDetailDto?> ResumeJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is not MultiRunJob job)
        {
            return null;
        }

        await job.Resume().ConfigureAwait(false);
        return await _projections.BuildDetailAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobDetailDto?> StopJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is not MultiRunJob job)
        {
            return null;
        }

        await job.Stop().ConfigureAwait(false);
        return await _projections.BuildDetailAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> AbortJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is not MultiRunJob job)
        {
            return false;
        }

        await job.Abort().ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DeleteJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = FindJob(jobId);
        if (job is null)
        {
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var entity = await jobRepo.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (entity is not null)
        {
            await jobRepo.DeleteAsync(entity, cancellationToken).ConfigureAwait(false);
        }

        if (job is MultiRunJob multiRunJob)
        {
            await _subscriptions.UnsubscribeAsync(multiRunJob).ConfigureAwait(false);
        }

        _jobManager.RemoveJob(job);
        await PublishJobNotificationAsync(job, $"Job '{job.Name}' deleted", "warning", cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<JobDetailDto?> UpdateBotsAsync(int jobId, int bots, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is not MultiRunJob job)
        {
            return null;
        }

        await job.ChangeBots(Math.Max(1, bots)).ConfigureAwait(false);
        return await _projections.BuildDetailAsync(job, cancellationToken).ConfigureAwait(false);
    }

    private async Task StartJobInternalAsync(MultiRunJob job, CancellationToken cancellationToken)
    {
        try
        {
            await job.Start(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Job {JobId} is already running", job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start job {JobId}", job.Id);
            await _notifications.PublishAsync(new NotificationDto(
                "jobs",
                $"Failed to start job '{job.Name}': {ex.Message}",
                "error",
                DateTime.UtcNow), cancellationToken).ConfigureAwait(false);
        }
    }

    private Job? FindJob(int jobId)
        => _jobManager.Jobs.FirstOrDefault(j => j.Id == jobId);

    private MultiRunJobOptions BuildOptions(JobCreateRequest request)
        => new()
        {
            Name = request.Name,
            ConfigId = request.ConfigId,
            Bots = Math.Max(1, request.Bots),
            Skip = Math.Max(0, request.Skip),
            ProxyMode = ParseProxyMode(request.ProxyMode),
            ShuffleProxies = request.ShuffleProxies,
            NeverBanProxies = request.NeverBanProxies,
            ConcurrentProxyMode = request.ConcurrentProxyMode,
            PeriodicReloadIntervalSeconds = Math.Max(0, request.PeriodicReloadIntervalSeconds),
            MarkAsToCheckOnAbort = request.MarkAsToCheckOnAbort
        };

    private static JobProxyMode ParseProxyMode(string value)
        => Enum.TryParse<JobProxyMode>(value, true, out var mode) ? mode : JobProxyMode.Default;

    private Task PublishJobNotificationAsync(Job job, string message, string severity, CancellationToken cancellationToken)
        => _notifications.PublishAsync(new NotificationDto("jobs", message, severity, DateTime.UtcNow), cancellationToken);
}
