using Microsoft.Extensions.DependencyInjection;
using Flux.Shared.Abstractions;
using Flux.Shared.Services;

namespace Flux.Shared.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFluxShared(this IServiceCollection services)
    {
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<IJobOrchestrator, JobOrchestrator>();
        services.AddSingleton<SettingsFacade>();
        services.AddSingleton<PluginService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<FluxApplication>();
        return services;
    }
}
