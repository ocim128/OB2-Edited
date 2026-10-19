using Microsoft.Extensions.DependencyInjection;
using Flux.Shared.Abstractions;
using Flux.Shared.Services;

namespace Flux.Shared.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFluxShared(this IServiceCollection services)
    {
        services.AddSingleton<IJobOrchestrator, JobOrchestrator>();
        services.AddSingleton<IDashboardService, DashboardService>();
        return services;
    }
}
