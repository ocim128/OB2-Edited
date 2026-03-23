using System;

namespace Flux.Shared.Models;

public record DesktopDashboardRefreshOptionsDto(
    TimeSpan StatisticsInterval,
    TimeSpan SystemMetricsInterval,
    int DatabaseQueryTimeoutSeconds,
    bool IsLowSpecMode);
