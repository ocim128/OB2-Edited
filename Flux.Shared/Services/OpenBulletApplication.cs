using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;

namespace Flux.Shared.Services;

public class FluxApplication
{
    public AuthenticationService Auth { get; }
    public IJobOrchestrator Jobs { get; }
    public SettingsFacade Settings { get; }
    public PluginService Plugins { get; }
    public NotificationService Notifications { get; }
    public DashboardService Dashboard { get; }

    private readonly ILogger<FluxApplication> _logger;

    public FluxApplication(
        AuthenticationService auth,
        IJobOrchestrator jobs,
        SettingsFacade settings,
        PluginService plugins,
        NotificationService notifications,
        DashboardService dashboard,
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
