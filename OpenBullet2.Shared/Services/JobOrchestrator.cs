using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenBullet2.Core.Entities;
using OpenBullet2.Core.Models.Data;
using OpenBullet2.Core.Models.Hits;
using OpenBullet2.Core.Models.Jobs;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Shared.Abstractions;
using OpenBullet2.Shared.Models;
using RuriLib.Models.Data.DataPools;
using RuriLib.Models.Hits;
using RuriLib.Models.Jobs;
using RuriLib.Models.Jobs.Status;

namespace OpenBullet2.Shared.Services;

public class JobOrchestrator : IJobOrchestrator
{
    private readonly JobManagerService _jobManager;
    private readonly JobFactoryService _jobFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notifications;
    private readonly ILogger<JobOrchestrator> _logger;
    private readonly JsonSerializerSettings _jsonSettings = new() { TypeNameHandling = TypeNameHandling.Auto };
    private readonly HashSet<int> _subscribedJobs = new();
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

        foreach (var job in _jobManager.Jobs.OfType<MultiRunJob>())
        {
            EnsureJobSubscribedAsync(job).GetAwaiter().GetResult();
        }
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
        await EnsureJobSubscribedAsync(job).ConfigureAwait(false);

        _logger.LogInformation("Created job {JobId} ({JobName})", entity.Id, job.Name);
        await _notifications.PublishAsync(
            new NotificationDto("jobs", $"Job '{job.Name}' created", "info", DateTime.UtcNow),
            cancellationToken).ConfigureAwait(false);

        if (request.AutoStart)
        {
            _ = Task.Run(async () => await StartJobInternalAsync(job, CancellationToken.None));
        }

        return await BuildDetailAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public Task<JobDetailDto?> GetJobAsync(int jobId, CancellationToken cancellationToken = default)
        => BuildDetailAsync(FindJob(jobId), cancellationToken);

    public Task<IReadOnlyList<JobSummaryDto>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        var list = _jobManager.Jobs.Select(ToSummary).ToList();
        return Task.FromResult((IReadOnlyList<JobSummaryDto>)list);
    }

    public Task<JobQueueDto> GetQueueSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var running = new List<JobSummaryDto>();
        var waiting = new List<JobSummaryDto>();
        var idle = new List<JobSummaryDto>();
        var paused = new List<JobSummaryDto>();
        var completed = new List<JobSummaryDto>();

        foreach (var job in _jobManager.Jobs)
        {
            var summary = ToSummary(job);
            switch (job.Status)
            {
                case JobStatus.Running:
                    running.Add(summary);
                    break;
                case JobStatus.Waiting:
                case JobStatus.Starting:
                    waiting.Add(summary);
                    break;
                case JobStatus.Paused:
                case JobStatus.Pausing:
                    paused.Add(summary);
                    break;
                case JobStatus.Stopping:
                    completed.Add(summary);
                    break;
                default:
                    idle.Add(summary);
                    break;
            }
        }

        var snapshot = new JobQueueDto(running, waiting, idle, paused, completed);
        return Task.FromResult(snapshot);
    }

    public async Task<IReadOnlyList<JobResultDto>> GetRecentResultsAsync(int jobId, int take = 200, CancellationToken cancellationToken = default)
    {
        var job = FindJob(jobId);
        if (job is null)
        {
            return Array.Empty<JobResultDto>();
        }

        using var scope = _scopeFactory.CreateScope();
        var hitRepo = scope.ServiceProvider.GetRequiredService<IHitRepository>();
        var query = hitRepo.GetAll();

        if (job is MultiRunJob multiRun)
        {
            query = FilterHitsForJob(query, multiRun);
        }

        var entities = await query
            .OrderByDescending(h => h.Date)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(ToDto).ToList();
    }

    public async Task<JobDetailDto?> StartJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        if (FindJob(jobId) is not MultiRunJob job)
        {
            return null;
        }

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

        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var entity = await jobRepo.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (entity is not null)
        {
            await jobRepo.DeleteAsync(entity, cancellationToken).ConfigureAwait(false);
        }

        _jobManager.RemoveJob(job);
        await PublishJobNotificationAsync(job, $"Job '{job.Name}' deleted", "warning", cancellationToken).ConfigureAwait(false);
        await UnsubscribeJobAsync(job.Id).ConfigureAwait(false);
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

    private JobSummaryDto ToSummary(Job job)
    {
        var progress = job switch
        {
            MultiRunJob multiRun => Math.Clamp(multiRun.Progress, 0, 1),
            ProxyCheckJob proxyJob => Math.Clamp(proxyJob.Progress, 0, 1),
            _ => 0
        };

        var bots = job switch
        {
            MultiRunJob multiRun => multiRun.Bots,
            ProxyCheckJob proxyJob => proxyJob.Bots,
            _ => 0
        };

        return new JobSummaryDto(
            job.Id,
            job.Name,
            (job as MultiRunJob)?.Config?.Metadata?.Name ?? job.GetType().Name,
            job.OwnerId == 0 ? "Admin" : job.OwnerId.ToString(),
            job.Status.ToString(),
            bots,
            progress,
            job.CreationTime,
            DateTime.UtcNow);
    }

    private async Task<JobDetailDto?> BuildDetailAsync(Job? job, CancellationToken cancellationToken)
    {
        if (job is null)
        {
            return null;
        }

        var results = await GetRecentResultsAsync(job.Id, 50, cancellationToken).ConfigureAwait(false);
        var bots = BuildBotStates(job);
        var counters = BuildCounters(job);
        var dataPool = DescribeDataPool(job);

        return new JobDetailDto(
            ToSummary(job),
            dataPool,
            results,
            counters,
            bots,
            Array.Empty<NotificationDto>());
    }

    private static IReadOnlyList<BotStateDto> BuildBotStates(Job job)
    {
        if (job is not MultiRunJob multiRun || multiRun.CurrentBotDatas is null)
        {
            return Array.Empty<BotStateDto>();
        }

        var list = new List<BotStateDto>(multiRun.CurrentBotDatas.Length);
        for (var i = 0; i < multiRun.CurrentBotDatas.Length; i++)
        {
            var data = multiRun.CurrentBotDatas[i];
            if (data is null)
            {
                continue;
            }

            list.Add(new BotStateDto(
                i + 1,
                data.Line?.Data,
                data.Proxy?.ToString(),
                data.ExecutionInfo));
        }

        return list;
    }

    private static JobCountersDto BuildCounters(Job job)
    {
        if (job is MultiRunJob multiRun)
        {
            var stats = multiRun.Statistics;
            return new JobCountersDto(
                stats.Hits,
                stats.Custom,
                stats.ToCheck,
                stats.Fails,
                multiRun.Bots,
                multiRun.CPM,
                Math.Clamp(multiRun.Progress, 0, 1));
        }

        if (job is ProxyCheckJob proxyJob)
        {
            return new JobCountersDto(
                proxyJob.Working,
                0,
                0,
                proxyJob.NotWorking,
                proxyJob.Bots,
                proxyJob.CPM,
                Math.Clamp(proxyJob.Progress, 0, 1));
        }

        return new JobCountersDto(0, 0, 0, 0, 0, 0, 0);
    }

    private static string DescribeDataPool(Job job)
    {
        if (job is not MultiRunJob multiRun || multiRun.DataPool is null)
        {
            return "Unknown";
        }

        return multiRun.DataPool switch
        {
            WordlistDataPool w => $"Wordlist: {w.Wordlist?.Name ?? "Unknown"}",
            FileDataPool f => $"File: {f.FileName}",
            RangeDataPool r => $"Range: {r.Start}-{r.Amount}",
            CombinationsDataPool c => $"Combinations: {c.CharSet} x {c.Length}",
            InfiniteDataPool => "Infinite",
            _ => multiRun.DataPool.GetType().Name
        };
    }

    private static JobResultDto ToDto(HitEntity entity)
        => new(entity.OwnerId, entity.Type, entity.Data, entity.CapturedData, entity.Proxy, entity.Date);

    private IQueryable<HitEntity> FilterHitsForJob(IQueryable<HitEntity> query, MultiRunJob job)
    {
        var configId = job.Config?.Id;
        query = !string.IsNullOrWhiteSpace(configId)
            ? query.Where(h => h.ConfigId == configId)
            : query;

        if (job.DataPool is WordlistDataPool wordlist)
        {
            var id = wordlist.Wordlist?.Id ?? -1;
            query = query.Where(h => h.WordlistId == id);
        }

        return query;
    }

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

    private async Task EnsureJobSubscribedAsync(MultiRunJob job)
    {
        await _subscriptionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_subscribedJobs.Contains(job.Id))
            {
                return;
            }

            job.OnStatusChanged += HandleStatusChanged;
            job.OnHit += HandleHit;
            job.OnError += HandleError;
            job.OnCompleted += HandleCompleted;
            _subscribedJobs.Add(job.Id);
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    private async Task UnsubscribeJobAsync(int jobId)
    {
        await _subscriptionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_subscribedJobs.Remove(jobId))
            {
                return;
            }

            if (FindJob(jobId) is MultiRunJob job)
            {
                job.OnStatusChanged -= HandleStatusChanged;
                job.OnHit -= HandleHit;
                job.OnError -= HandleError;
                job.OnCompleted -= HandleCompleted;
            }
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

        _ = PublishJobNotificationAsync(job,
            $"Job '{job.Name}' status changed to {status}",
            status is JobStatus.Running ? "success" : "info",
            CancellationToken.None);
    }

    private void HandleHit(object sender, Hit hit)
    {
        _ = _notifications.PublishAsync(new NotificationDto(
            "hits",
            $"[{hit.Type}] {hit.DataString}",
            "success",
            DateTime.UtcNow), CancellationToken.None);
    }

    private void HandleError(object sender, Exception exception)
    {
        if (sender is not Job job)
        {
            return;
        }

        _ = PublishJobNotificationAsync(job,
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

        _ = PublishJobNotificationAsync(job,
            $"Job '{job.Name}' completed",
            "success",
            CancellationToken.None);
    }

    private Task PublishJobNotificationAsync(Job job, string message, string severity, CancellationToken cancellationToken)
        => _notifications.PublishAsync(new NotificationDto("jobs", message, severity, DateTime.UtcNow), cancellationToken);
}
