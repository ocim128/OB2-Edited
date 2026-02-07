using System.Threading;
using System.Threading.Tasks;

namespace Flux.Shared.Abstractions;

public interface IFluxApplication
{
    IAuthenticationService Auth { get; }
    IJobOrchestrator Jobs { get; }
    ISettingsFacade Settings { get; }
    IPluginService Plugins { get; }
    INotificationService Notifications { get; }
    IDashboardService Dashboard { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
}
