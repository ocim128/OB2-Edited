using System.Threading;
using System.Threading.Tasks;

namespace OpenBullet2.Shared.Abstractions;

public interface IOpenBulletApplication
{
    IAuthenticationService Auth { get; }
    IJobOrchestrator Jobs { get; }
    ISettingsFacade Settings { get; }
    IPluginService Plugins { get; }
    INotificationService Notifications { get; }
    IDashboardService Dashboard { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
}
