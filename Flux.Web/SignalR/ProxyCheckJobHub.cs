using Flux.Core.Services;
using Flux.Web.Interfaces;
using Flux.Web.Services;

namespace Flux.Web.SignalR;

/// <summary>
/// SignalR hub for a proxy check job.
/// </summary>
public class ProxyCheckJobHub : JobHub
{
    /// <summary></summary>
    public ProxyCheckJobHub(IAuthTokenService tokenService,
        ILogger<ProxyCheckJobHub> logger, ProxyCheckJobService jobService,
        FluxSettingsService fluxSettingsService)
        : base(tokenService, logger, jobService, fluxSettingsService)
    {
    }
}
