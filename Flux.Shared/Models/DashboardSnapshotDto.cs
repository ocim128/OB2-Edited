using System.Collections.Generic;

namespace Flux.Shared.Models;

public record DashboardSnapshotDto(
    IReadOnlyList<JobSummaryDto> ActiveJobs,
    IReadOnlyList<JobResultDto> RecentHits,
    System.Collections.Generic.IDictionary<string, double> Metrics);
