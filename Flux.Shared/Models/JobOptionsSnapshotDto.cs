using Flux.Core.Models.Jobs;

namespace Flux.Shared.Models;

public record JobOptionsSnapshotDto(
    int JobId,
    JobType JobType,
    JobOptions Options);
