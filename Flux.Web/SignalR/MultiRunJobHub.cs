using Flux.Core.Services;
using Flux.Web.Interfaces;
using Flux.Web.Services;

namespace Flux.Web.SignalR;

/// <summary>
/// SignalR hub for a multi run job.
/// </summary>
public class MultiRunJobHub : JobHub
{
    /// <summary></summary>
    public MultiRunJobHub(IAuthTokenService tokenService,
        ILogger<MultiRunJobHub> logger, MultiRunJobService jobService,
        FluxSettingsService fluxSettingsService)
        : base(tokenService, logger, jobService, fluxSettingsService)
    {
    }
}
