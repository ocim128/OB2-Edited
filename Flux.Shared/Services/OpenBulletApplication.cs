using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;

namespace Flux.Shared.Services;

public class FluxApplication : IFluxApplication
{
    public IAuthenticationService Auth { get; }
    public IJobOrchestrator Jobs { get; }
    public ISettingsFacade Settings { get; }
    public IPluginService Plugins { get; }
    public INotificationService Notifications { get; }
    public IDashboardService Dashboard { get; }

    private readonly ILogger<FluxApplication> _logger;

    public FluxApplication(
        IAuthenticationService auth,
        IJobOrchestrator jobs,
        ISettingsFacade settings,
        IPluginService plugins,
        INotificationService notifications,
        IDashboardService dashboard,
        ILogger<FluxApplication> logger)
    {
        Auth = auth;
        Jobs = jobs;
        Settings = settings;
        Plugins = plugins;
        Notifications = notifications;
        Dashboard = dashboard;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Auth.EnsureSeedUserAsync(new RegisterRequest("admin", "admin", "Admin"), cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation("Flux shared services initialized");
    }
}
