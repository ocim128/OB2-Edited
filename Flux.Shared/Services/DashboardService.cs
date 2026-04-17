using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flux.Core.Repositories;
using Flux.Core.Services;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RuriLib.Models.Jobs;
using RuriLib.Models.Jobs.Status;
using RuriLib.Services;

namespace Flux.Shared.Services;

public class DashboardService : IDashboardService
{
    private readonly JobManagerService _jobManager;
    private readonly IHitRepository _hitRepository;
    private readonly IJobRepository _jobRepository;
    private readonly IConfigRepository _configRepository;
    private readonly IProxyGroupRepository _proxyGroupRepository;
    private readonly IProxyRepository _proxyRepository;
    private readonly IWordlistRepository _wordlistRepository;
    private readonly IGuestRepository _guestRepository;
    private readonly PluginRepository _pluginRepository;
    private readonly IConfiguration _configuration;

    public DashboardService(
        JobManagerService jobManager,
        IHitRepository hitRepository,
        IJobRepository jobRepository,
        IConfigRepository configRepository,
        IProxyGroupRepository proxyGroupRepository,
        IProxyRepository proxyRepository,
        IWordlistRepository wordlistRepository,
        IGuestRepository guestRepository,
        PluginRepository pluginRepository,
        IConfiguration configuration)
    {
        _jobManager = jobManager;
        _hitRepository = hitRepository;
        _jobRepository = jobRepository;
        _configRepository = configRepository;
        _proxyGroupRepository = proxyGroupRepository;
        _proxyRepository = proxyRepository;
        _wordlistRepository = wordlistRepository;
        _guestRepository = guestRepository;
        _pluginRepository = pluginRepository;
        _configuration = configuration;
    }

    public async Task<DashboardSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var activeJobs = _jobManager.Jobs
            .Where(j => j.Status is JobStatus.Running or JobStatus.Starting)
            .Select(j => new JobSummaryDto(
                j.Id,
                j.Name,
                (j as MultiRunJob)?.Config?.Metadata?.Name ?? j.GetType().Name,
                j.OwnerId == 0 ? "Admin" : j.OwnerId.ToString(),
                j.Status.ToString(),
                j is MultiRunJob multiRun ? multiRun.Bots : 0,
                j is MultiRunJob m ? Math.Clamp(m.Progress, 0, 1) : 0,
                j.CreationTime,
                j.Status == JobStatus.Running ? System.DateTime.UtcNow : null))
            .ToList();

        var hits = await _hitRepository.GetAll()
            .AsNoTracking()
            .OrderByDescending(h => h.Date)
            .Take(20)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var metrics = new Dictionary<string, double>
        {
            ["jobs.total"] = _jobManager.Jobs.Count(),
            ["jobs.running"] = activeJobs.Count,
            ["hits.total"] = await _hitRepository.CountAsync().ConfigureAwait(false)
        };

        var recentHits = hits.Select(h => new JobResultDto(h.OwnerId, h.Type, h.Data, h.CapturedData, h.Proxy, h.Date)).ToList();

        return new DashboardSnapshotDto(activeJobs, recentHits, metrics);
    }

    public DesktopDashboardRefreshOptionsDto GetDesktopRefreshOptions()
    {
        var performance = _configuration.GetSection("Performance");
        var statisticsInterval = ReadInt(performance, "StatisticsUpdateInterval", 45);
        var systemMetricsInterval = ReadInt(performance, "SystemMetricsUpdateInterval", 8);
        var databaseQueryTimeout = ReadInt(performance, "DatabaseQueryTimeout", 10);
        var isLowSpecMode = ReadBool(performance, "LowSpecMode", false);

        if (isLowSpecMode)
        {
            statisticsInterval = System.Math.Max(statisticsInterval, 60);
            systemMetricsInterval = System.Math.Max(systemMetricsInterval, 10);
            databaseQueryTimeout = System.Math.Max(databaseQueryTimeout, 15);
        }

        return new DesktopDashboardRefreshOptionsDto(
            System.TimeSpan.FromSeconds(statisticsInterval),
            System.TimeSpan.FromSeconds(systemMetricsInterval),
            databaseQueryTimeout,
            isLowSpecMode);
    }

    public async Task<DesktopDashboardSnapshotDto> GetDesktopSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var jobsTask = _jobRepository.GetAll().CountAsync(cancellationToken);
        var configsTask = CountConfigsAsync();
        var hitsTask = _hitRepository.CountAsync();
        var guestsTask = _guestRepository.GetAll().CountAsync(cancellationToken);
        var proxiesTask = CountProxiesAsync(cancellationToken);
        var wordlistsTask = CountWordlistsAsync(cancellationToken);
        var pluginsTask = Task.Run(() => _pluginRepository.GetPluginNames().Count(), cancellationToken);

        await Task.WhenAll(jobsTask, configsTask, hitsTask, guestsTask, proxiesTask, wordlistsTask, pluginsTask)
            .ConfigureAwait(false);

        return new DesktopDashboardSnapshotDto(
            jobsTask.Result,
            configsTask.Result,
            (int)System.Math.Min(hitsTask.Result, int.MaxValue),
            proxiesTask.Result,
            wordlistsTask.Result.Count,
            wordlistsTask.Result.TotalLines,
            guestsTask.Result,
            pluginsTask.Result);
    }

    private async Task<int> CountConfigsAsync()
        => (await _configRepository.GetAllAsync().ConfigureAwait(false))?.Count() ?? 0;

    private async Task<int> CountProxiesAsync(CancellationToken cancellationToken)
        => await _proxyRepository.GetAll().CountAsync(cancellationToken).ConfigureAwait(false);

    private async Task<(int Count, long TotalLines)> CountWordlistsAsync(CancellationToken cancellationToken)
    {
        var count = await _wordlistRepository.GetAll().CountAsync(cancellationToken).ConfigureAwait(false);
        var total = await _wordlistRepository.GetAll().SumAsync(w => (long?)w.Total, cancellationToken).ConfigureAwait(false) ?? 0L;
        return (count, total);
    }

    private static int ReadInt(IConfigurationSection section, string key, int fallback)
        => int.TryParse(section[key], out var parsed) ? parsed : fallback;

    private static bool ReadBool(IConfigurationSection section, string key, bool fallback)
        => bool.TryParse(section[key], out var parsed) ? parsed : fallback;
}
