using System.Threading;
using System.Threading.Tasks;
using Flux.Shared.Models;

namespace Flux.Shared.Abstractions;

public interface IDashboardService
{
    Task<DashboardSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
