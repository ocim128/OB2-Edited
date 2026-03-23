using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flux.Core.Repositories;
using Flux.Shared.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Flux.Shared.Services;

public class JobQueries : IJobQueries
{
    private readonly IProxyGroupRepository _proxyGroupRepository;

    public JobQueries(IProxyGroupRepository proxyGroupRepository)
    {
        _proxyGroupRepository = proxyGroupRepository;
    }

    public async Task<IReadOnlyDictionary<int, string>> GetProxyGroupNamesAsync(CancellationToken cancellationToken = default)
    {
        var groups = await _proxyGroupRepository.GetAll()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lookup = groups.ToDictionary(group => group.Id, group => group.Name);
        lookup[-1] = "All";
        return lookup;
    }
}
