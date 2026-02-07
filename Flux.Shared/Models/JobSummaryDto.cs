using System;

namespace Flux.Shared.Models;

public record JobSummaryDto(
    int Id,
    string Name,
    string ConfigName,
    string Owner,
    string Status,
    int Bots,
    double Progress,
    DateTime CreatedAt,
    DateTime? LastUpdatedAt);
