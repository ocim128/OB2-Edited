namespace Flux.Shared.Models;

public record DesktopDashboardSnapshotDto(
    int JobsCount,
    int ConfigsCount,
    int HitsCount,
    int ProxiesCount,
    int WordlistsCount,
    long WordlistLines,
    int GuestsCount,
    int PluginsCount);
