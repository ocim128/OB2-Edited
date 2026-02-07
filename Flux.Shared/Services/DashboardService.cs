using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Flux.Core.Repositories;
using Flux.Core.Services;
using RuriLib.Models.Jobs;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;
using RuriLib.Models.Jobs.Status;

namespace Flux.Shared.Services;

public class DashboardService : IDashboardService
{
    private readonly JobManagerService _jobManager;
    private readonly IServiceScopeFactory _scopeFactory;

    public DashboardService(JobManagerService jobManager, IServiceScopeFactory scopeFactory)
    {
        _jobManager = jobManager;
        _scopeFactory = scopeFactory;
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

        using var scope = _scopeFactory.CreateScope();
        var hitRepo = scope.ServiceProvider.GetRequiredService<IHitRepository>();

        var hits = await hitRepo.GetAll()
            .OrderByDescending(h => h.Date)
            .Take(20)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var metrics = new Dictionary<string, double>
        {
            ["jobs.total"] = _jobManager.Jobs.Count(),
            ["jobs.running"] = activeJobs.Count,
            ["hits.total"] = await hitRepo.CountAsync().ConfigureAwait(false)
        };

        var recentHits = hits.Select(h => new JobResultDto(h.OwnerId, h.Type, h.Data, h.CapturedData, h.Proxy, h.Date)).ToList();

        return new DashboardSnapshotDto(activeJobs, recentHits, metrics);
    }
}
