using Microsoft.Extensions.DependencyInjection;
using Flux.Shared.Abstractions;
using Flux.Shared.Services;

namespace Flux.Shared.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFluxShared(this IServiceCollection services)
    {
        services.AddSingleton<JobProjectionService>();
        services.AddSingleton<JobEventSubscriptionService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IJobCommands, JobCommands>();
        services.AddSingleton<IJobQueries, JobQueries>();
        services.AddSingleton<IJobOrchestrator, JobOrchestrator>();
        services.AddSingleton<ISettingsFacade, SettingsFacade>();
        services.AddSingleton<IPluginService, PluginService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<IFluxApplication, FluxApplication>();
        return services;
    }
}
