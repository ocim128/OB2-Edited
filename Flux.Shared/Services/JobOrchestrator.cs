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
using Microsoft.EntityFrameworkCore;
using RuriLib.Models.Hits;
using RuriLib.Models.Jobs;
using RuriLib.Models.Jobs.Status;

namespace Flux.Shared.Services;

public partial class JobOrchestrator : IJobOrchestrator
{
    private readonly JobManagerService _jobManager;
    private readonly JobFactoryService _jobFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notifications;
    private readonly ILogger<JobOrchestrator> _logger;
    private readonly JsonSerializerSettings _jsonSettings = new() { TypeNameHandling = TypeNameHandling.Auto };
    private readonly SemaphoreSlim _proxyGroupLock = new(1, 1);
    private IReadOnlyDictionary<int, string>? _proxyGroupNames;

    // Subscription state (moved from JobEventSubscriptionService)
    private readonly Dictionary<int, MultiRunJob> _subscribedJobs = new();
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);

    public JobOrchestrator(
        JobManagerService jobManager,
        JobFactoryService jobFactory,
        IServiceScopeFactory scopeFactory,
        INotificationService notifications,
        ILogger<JobOrchestrator> logger)
    {
        _jobManager = jobManager;
        _jobFactory = jobFactory;
        _scopeFactory = scopeFactory;
        _notifications = notifications;
        _logger = logger;

        SubscribeExistingJobs(_jobManager.Jobs.OfType<MultiRunJob>());
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
        if (job is MultiRunJob mrj) EnsureSubscribed(mrj);

        _logger.LogInformation("Created job {JobId} ({JobName})", entity.Id, job.Name);
        await _notifications.PublishAsync(
            new NotificationDto("jobs", $"Job '{job.Name}' created", "info", DateTime.UtcNow),
            cancellationToken).ConfigureAwait(false);

        if (request.AutoStart)
        {
            _ = Task.Run(async () => await StartJobInternalAsync(job, CancellationToken.None));
        }

        return await BuildDetailAsync(job, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Failed to build details for job {job.Id}");
    }

    public async Task<JobOptionsSnapshotDto?> GetJobOptionsAsync(int jobId, bool clone = false, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var entity = await jobRepo.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        var wrapper = JsonConvert.DeserializeObject<JobOptionsWrapper>(entity.JobOptions, _jsonSettings)
            ?? throw new InvalidOperationException($"Could not deserialize options for job {jobId}");

        var options = clone
            ? JobOptionsFactory.CloneExistant(wrapper.Options)
            : wrapper.Options;

        return new JobOptionsSnapshotDto(entity.Id, entity.JobType, options);
    }

    public async Task<int> CreateJobAsync(JobOptions options, CancellationToken cancellationToken = default)
    {
        var jobType = ResolveJobType(options);
        var entity = await CreateJobEntityAsync(jobType, options, cancellationToken).ConfigureAwait(false);
        var job = await MaterializeJobAsync(entity.Id, options).ConfigureAwait(false);

        _jobManager.AddJob(job);
        if (job is MultiRunJob mrj3) EnsureSubscribed(mrj3);

        _logger.LogInformation("Created job {JobId} ({JobName})", job.Id, job.Name);
        await PublishJobNotificationAsync(job, $"Job '{job.Name}' created", "info", cancellationToken).ConfigureAwait(false);
        return job.Id;
    }

    public async Task<int?> UpdateJobAsync(int jobId, JobOptions options, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var entity = await jobRepo.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        entity.JobType = ResolveJobType(options);
        entity.JobOptions = SerializeOptions(options);
        await jobRepo.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);

        var oldJob = FindJob(jobId) ?? throw new InvalidOperationException($"Job {jobId} is not loaded");
        UnsubscribeIfMultiRun(oldJob);

        var newJob = await MaterializeJobAsync(jobId, options).ConfigureAwait(false);
        _jobManager.RemoveJob(oldJob);
        _jobManager.AddJob(newJob);
        if (newJob is MultiRunJob mrj2) EnsureSubscribed(mrj2);

        _logger.LogInformation("Updated job {JobId} ({JobName})", newJob.Id, newJob.Name);
        await PublishJobNotificationAsync(newJob, $"Job '{newJob.Name}' updated", "info", cancellationToken).ConfigureAwait(false);
        return newJob.Id;
    }

    public Task<JobDetailDto?> GetJobAsync(int jobId, CancellationToken cancellationToken = default)
        => BuildDetailAsync(FindJob(jobId), cancellationToken);

    public Task<IReadOnlyList<JobSummaryDto>> GetJobsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(BuildSummaries(_jobManager.Jobs));

    public Task<JobQueueDto> GetQueueSnapshotAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(BuildQueueSnapshot(_jobManager.Jobs));

    public async Task<IReadOnlyList<JobResultDto>> GetRecentResultsAsync(int jobId, int take = 200, CancellationToken cancellationToken = default)
        => await GetRecentResultsFromDbAsync(FindJob(jobId), take, cancellationToken).ConfigureAwait(false);

    public async Task<JobDetailDto?> StartJobAsync(int jobId, IReadOnlyDictionary<string, string>? customInputs = null, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is not MultiRunJob job)
        {
            return null;
        }

        ApplyCustomInputs(job, customInputs);

        await StartJobInternalAsync(job, cancellationToken).ConfigureAwait(false);
        return await BuildDetailAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobDetailDto?> PauseJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is not MultiRunJob job)
        {
            return null;
        }

        await job.Pause().ConfigureAwait(false);
        return await BuildDetailAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobDetailDto?> ResumeJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is not MultiRunJob job)
        {
            return null;
        }

        await job.Resume().ConfigureAwait(false);
        return await BuildDetailAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobDetailDto?> StopJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is not MultiRunJob job)
        {
            return null;
        }

        await job.Stop().ConfigureAwait(false);
        return await BuildDetailAsync(job, cancellationToken).ConfigureAwait(false);
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

        if (job.Status != RuriLib.Models.Jobs.JobStatus.Idle)
        {
            throw new InvalidOperationException("The job is not idle, please stop/abort the job first!");
        }

        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var entity = await jobRepo.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (entity is not null)
        {
            await jobRepo.DeleteAsync(entity, cancellationToken).ConfigureAwait(false);
        }

        UnsubscribeIfMultiRun(job);

        _jobManager.RemoveJob(job);
        await PublishJobNotificationAsync(job, $"Job '{job.Name}' deleted", "warning", cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DeleteAllJobsAsync(CancellationToken cancellationToken = default)
    {
        var jobs = _jobManager.Jobs.ToList();
        var nonIdleJob = jobs.FirstOrDefault(j => j.Status != RuriLib.Models.Jobs.JobStatus.Idle);
        if (nonIdleJob is not null)
        {
            throw new InvalidOperationException($"The job #{nonIdleJob.Id} is not idle, please stop/abort the job first!");
        }

        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        jobRepo.Purge();

        foreach (var job in jobs)
        {
            UnsubscribeIfMultiRun(job);
        }

        _jobManager.Clear();
        await _notifications.PublishAsync(
            new NotificationDto("jobs", "All jobs deleted", "warning", DateTime.UtcNow),
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<JobDetailDto?> UpdateBotsAsync(int jobId, int bots, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is not MultiRunJob job)
        {
            return null;
        }

        await job.ChangeBots(Math.Max(1, bots)).ConfigureAwait(false);
        return await BuildDetailAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<DesktopJobListItemDto>> GetDesktopJobsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(BuildDesktopListItems(_jobManager.Jobs));

    public async Task<MultiRunJobViewerSnapshotDto?> GetMultiRunJobViewerSnapshotAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = FindJob(jobId);
        if (job is null)
        {
            return null;
        }

        var proxyGroupNames = await GetProxyGroupNamesCachedAsync(cancellationToken).ConfigureAwait(false);
        return BuildMultiRunViewerSnapshot(job, proxyGroupNames);
    }

    public Task<BotLogDto?> GetBotLogAsync(int jobId, string resultId, CancellationToken cancellationToken = default)
        => Task.FromResult(BuildBotLog(FindJob(jobId), resultId));

    public Task SkipWaitAsync(int jobId, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is MultiRunJob job)
        {
            job.SkipWait();
        }
        return Task.CompletedTask;
    }

    public Task ResetSkipAsync(int jobId, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is MultiRunJob job)
        {
            if (job.Status is not JobStatus.Idle)
            {
                return Task.CompletedTask;
            }

            job.Skip = 0;
            job.DataPool?.Reload();
        }
        return Task.CompletedTask;
    }

    // ── Private helpers ──────────────────────────────────────────────

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

    private async Task<JobEntity> CreateJobEntityAsync(JobType jobType, JobOptions options, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        var entity = new JobEntity
        {
            CreationDate = DateTime.UtcNow,
            JobType = jobType,
            JobOptions = SerializeOptions(options)
        };

        await jobRepo.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        return entity;
    }

    private async Task<Job> MaterializeJobAsync(int jobId, JobOptions options)
        => await _jobFactory.FromOptionsAsync(jobId, ownerId: 0, options).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Failed to create runtime job for {jobId}");

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

    private string SerializeOptions(JobOptions options)
        => JsonConvert.SerializeObject(new JobOptionsWrapper { Options = options }, _jsonSettings);

    private static JobType ResolveJobType(JobOptions options) => options switch
    {
        MultiRunJobOptions => JobType.MultiRun,
        ProxyCheckJobOptions => JobType.ProxyCheck,
        _ => throw new NotImplementedException()
    };

    private Task PublishJobNotificationAsync(Job job, string message, string severity, CancellationToken cancellationToken)
        => _notifications.PublishAsync(new NotificationDto("jobs", message, severity, DateTime.UtcNow), cancellationToken);

    private async Task<IReadOnlyDictionary<int, string>> GetProxyGroupNamesCachedAsync(CancellationToken cancellationToken)
    {
        if (_proxyGroupNames is not null)
        {
            return _proxyGroupNames;
        }

        await _proxyGroupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_proxyGroupNames is not null)
            {
                return _proxyGroupNames;
            }

            using var scope = _scopeFactory.CreateScope();
            var proxyGroupRepository = scope.ServiceProvider.GetRequiredService<IProxyGroupRepository>();
            var groups = await proxyGroupRepository.GetAll()
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var lookup = groups.ToDictionary(group => group.Id, group => group.Name);
            lookup[-1] = "All";
            _proxyGroupNames = lookup;
            return _proxyGroupNames;
        }
        finally
        {
            _proxyGroupLock.Release();
        }
    }

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

    // ── Event subscriptions (moved from JobEventSubscriptionService) ─

    private void SubscribeExistingJobs(IEnumerable<MultiRunJob> jobs)
    {
        foreach (var job in jobs)
        {
            EnsureSubscribed(job);
        }
    }

    private void EnsureSubscribed(MultiRunJob job)
    {
        lock (_subscribedJobs)
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
    }

    private void UnsubscribeIfMultiRun(Job job)
    {
        if (job is not MultiRunJob multiRun)
        {
            return;
        }

        lock (_subscribedJobs)
        {
            if (!_subscribedJobs.Remove(multiRun.Id))
            {
                return;
            }

            multiRun.OnStatusChanged -= HandleStatusChanged;
            multiRun.OnHit -= HandleHit;
            multiRun.OnError -= HandleError;
            multiRun.OnCompleted -= HandleCompleted;
        }
    }

    private void HandleStatusChanged(object sender, JobStatus status)
    {
        if (sender is not Job job) return;
        _ = PublishJobNotificationAsync(job, $"Job '{job.Name}' status changed to {status}",
            status is JobStatus.Running ? "success" : "info", CancellationToken.None);
    }

    private void HandleHit(object sender, Hit hit)
    {
        _ = _notifications.PublishAsync(
            new NotificationDto("hits", $"[{hit.Type}] {hit.DataString}", "success", DateTime.UtcNow),
            CancellationToken.None);
    }

    private void HandleError(object sender, Exception exception)
    {
        if (sender is not Job job) return;
        _ = PublishJobNotificationAsync(job, $"Job '{job.Name}' error: {exception.Message}", "error", CancellationToken.None);
    }

    private void HandleCompleted(object sender, EventArgs e)
    {
        if (sender is not Job job) return;
        _ = PublishJobNotificationAsync(job, $"Job '{job.Name}' completed", "success", CancellationToken.None);
    }
}
