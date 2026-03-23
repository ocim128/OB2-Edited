using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Flux.Shared.Models;

namespace Flux.Shared.Abstractions;

public interface IJobQueries
{
    Task<IReadOnlyList<DesktopJobListItemDto>> GetDesktopJobsAsync(CancellationToken cancellationToken = default);
    Task<MultiRunJobViewerSnapshotDto?> GetMultiRunJobViewerSnapshotAsync(int jobId, CancellationToken cancellationToken = default);
    Task<BotLogDto?> GetBotLogAsync(int jobId, string resultId, CancellationToken cancellationToken = default);
}
