using System.Collections.Generic;

namespace Flux.Shared.Models;

public record JobQueueDto(
    IReadOnlyList<JobSummaryDto> Running,
    IReadOnlyList<JobSummaryDto> Waiting,
    IReadOnlyList<JobSummaryDto> Idle,
    IReadOnlyList<JobSummaryDto> Paused,
    IReadOnlyList<JobSummaryDto> Completed);
