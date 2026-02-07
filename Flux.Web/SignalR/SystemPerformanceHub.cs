using Flux.Core.Services;
using Flux.Web.Interfaces;
using Flux.Web.Services;

namespace Flux.Web.SignalR;

/// <summary>
/// SignalR hub for system performance monitoring.
/// </summary>
public class SystemPerformanceHub : AuthorizedHub
{
    private readonly PerformanceMonitorService _performanceMonitorService;

    /// <summary></summary>
    public SystemPerformanceHub(IAuthTokenService tokenService,
        FluxSettingsService fluxSettingsService,
        PerformanceMonitorService performanceMonitorService)
        : base(tokenService, fluxSettingsService, false)
    {
        _performanceMonitorService = performanceMonitorService;
    }

    /// <inheritdoc />
    public async override Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        await _performanceMonitorService.RegisterConnectionAsync(Context.ConnectionId);
    }

    /// <inheritdoc />
    public async override Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
        await _performanceMonitorService.UnregisterConnectionAsync(Context.ConnectionId);
    }
}
