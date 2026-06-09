using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Flux.Core.Models.Jobs;
using Flux.Shared.Models;

namespace Flux.Shared.Abstractions;

public interface IJobOrchestrator
{
    Task<JobDetailDto> CreateMultiRunJobAsync(JobCreateRequest request, CancellationToken cancellationToken = default);
    Task<JobOptionsSnapshotDto?> GetJobOptionsAsync(int jobId, bool clone = false, CancellationToken cancellationToken = default);
    Task<int> CreateJobAsync(JobOptions options, CancellationToken cancellationToken = default);
    Task<int?> UpdateJobAsync(int jobId, JobOptions options, CancellationToken cancellationToken = default);
    Task<JobDetailDto?> GetJobAsync(int jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JobSummaryDto>> GetJobsAsync(CancellationToken cancellationToken = default);
    Task<JobQueueDto> GetQueueSnapshotAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JobResultDto>> GetRecentResultsAsync(int jobId, int take = 200, CancellationToken cancellationToken = default);
    Task<JobDetailDto?> StartJobAsync(int jobId, IReadOnlyDictionary<string, string>? customInputs = null, CancellationToken cancellationToken = default);
    Task<JobDetailDto?> PauseJobAsync(int jobId, CancellationToken cancellationToken = default);
    Task<JobDetailDto?> ResumeJobAsync(int jobId, CancellationToken cancellationToken = default);
    Task<JobDetailDto?> StopJobAsync(int jobId, CancellationToken cancellationToken = default);
    Task<bool> AbortJobAsync(int jobId, CancellationToken cancellationToken = default);
    Task<bool> DeleteJobAsync(int jobId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAllJobsAsync(CancellationToken cancellationToken = default);
    Task<JobDetailDto?> UpdateBotsAsync(int jobId, int bots, CancellationToken cancellationToken = default);
    Task SkipWaitAsync(int jobId, CancellationToken cancellationToken = default);
    Task ResetSkipAsync(int jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DesktopJobListItemDto>> GetDesktopJobsAsync(CancellationToken cancellationToken = default);
    Task<MultiRunJobViewerSnapshotDto?> GetMultiRunJobViewerSnapshotAsync(int jobId, CancellationToken cancellationToken = default);
    Task<BotLogDto?> GetBotLogAsync(int jobId, string resultId, CancellationToken cancellationToken = default);
}
