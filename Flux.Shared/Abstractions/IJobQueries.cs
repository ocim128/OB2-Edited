using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Flux.Shared.Abstractions;

public interface IJobQueries
{
    Task<IReadOnlyDictionary<int, string>> GetProxyGroupNamesAsync(CancellationToken cancellationToken = default);
}
