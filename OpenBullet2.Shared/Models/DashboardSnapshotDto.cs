using System.Collections.Generic;

namespace OpenBullet2.Shared.Models;

public record DashboardSnapshotDto(
    IReadOnlyList<JobSummaryDto> ActiveJobs,
    IReadOnlyList<JobResultDto> RecentHits,
    System.Collections.Generic.IDictionary<string, double> Metrics);
