using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenBullet2.Shared.Abstractions;
using OpenBullet2.Shared.Models;

namespace OpenBullet2.Shared.Services;

public class OpenBulletApplication : IOpenBulletApplication
{
    public IAuthenticationService Auth { get; }
    public IJobOrchestrator Jobs { get; }
    public ISettingsFacade Settings { get; }
    public IPluginService Plugins { get; }
    public INotificationService Notifications { get; }
    public IDashboardService Dashboard { get; }

    private readonly ILogger<OpenBulletApplication> _logger;

    public OpenBulletApplication(
        IAuthenticationService auth,
        IJobOrchestrator jobs,
        ISettingsFacade settings,
        IPluginService plugins,
        INotificationService notifications,
        IDashboardService dashboard,
        ILogger<OpenBulletApplication> logger)
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
        _logger.LogInformation("OpenBullet shared services initialized");
    }
}
