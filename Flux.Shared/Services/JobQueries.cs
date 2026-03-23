using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flux.Core.Repositories;
using Flux.Core.Services;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;
using Microsoft.EntityFrameworkCore;
using RuriLib.Models.Jobs;

namespace Flux.Shared.Services;

public class JobQueries : IJobQueries
{
    private readonly JobManagerService _jobManager;
    private readonly JobProjectionService _projections;
    private readonly IProxyGroupRepository _proxyGroupRepository;
    private IReadOnlyDictionary<int, string>? _proxyGroupNames;

    public JobQueries(
        JobManagerService jobManager,
        JobProjectionService projections,
        IProxyGroupRepository proxyGroupRepository)
    {
        _jobManager = jobManager;
        _projections = projections;
        _proxyGroupRepository = proxyGroupRepository;
    }

    public Task<IReadOnlyList<DesktopJobListItemDto>> GetDesktopJobsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_projections.BuildDesktopListItems(_jobManager.Jobs));

    public async Task<MultiRunJobViewerSnapshotDto?> GetMultiRunJobViewerSnapshotAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = FindJob(jobId);
        if (job is null)
        {
            return null;
        }

        var proxyGroupNames = await GetProxyGroupNamesCachedAsync(cancellationToken).ConfigureAwait(false);
        return _projections.BuildMultiRunViewerSnapshot(job, proxyGroupNames);
    }

    public Task<BotLogDto?> GetBotLogAsync(int jobId, string resultId, CancellationToken cancellationToken = default)
        => Task.FromResult(_projections.BuildBotLog(FindJob(jobId), resultId));

    private Job? FindJob(int jobId)
        => _jobManager.Jobs.FirstOrDefault(job => job.Id == jobId);

    private async Task<IReadOnlyDictionary<int, string>> GetProxyGroupNamesCachedAsync(CancellationToken cancellationToken)
    {
        if (_proxyGroupNames is not null)
        {
            return _proxyGroupNames;
        }

        var groups = await _proxyGroupRepository.GetAll()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lookup = groups.ToDictionary(group => group.Id, group => group.Name);
        lookup[-1] = "All";
        _proxyGroupNames = lookup;
        return _proxyGroupNames;
    }
}
