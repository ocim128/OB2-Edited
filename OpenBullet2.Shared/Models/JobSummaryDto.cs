using System;

namespace OpenBullet2.Shared.Models;

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
