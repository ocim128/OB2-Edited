using System.Threading;
using System.Threading.Tasks;
using OpenBullet2.Shared.Models;

namespace OpenBullet2.Shared.Abstractions;

public interface IDashboardService
{
    Task<DashboardSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
