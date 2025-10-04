using Microsoft.Extensions.DependencyInjection;
using OpenBullet2.Shared.Abstractions;
using OpenBullet2.Shared.Services;

namespace OpenBullet2.Shared.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenBullet2Shared(this IServiceCollection services)
    {
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IJobOrchestrator, JobOrchestrator>();
        services.AddSingleton<ISettingsFacade, SettingsFacade>();
        services.AddSingleton<IPluginService, PluginService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<IOpenBulletApplication, OpenBulletApplication>();
        return services;
    }
}
