using Flux.Core;
using Flux.Core.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flux.Shared.Tests.DependencyInjection;

/// <summary>
/// Documents and pins the current repository lifetimes.
/// Repositories are singletons that consume a scoped <see cref="ApplicationDbContext"/>, so every
/// consumer shares one long-lived context. That is a real constraint: enabling
/// ValidateScopes/ValidateOnBuild throws "Cannot consume scoped service ... from singleton", and
/// converting to scoped requires making the ~40 repository consumers scoped as well.
/// These tests exist so that constraint is visible and a partial fix cannot land silently.
/// </summary>
public class RepositoryLifetimeTests
{
    private static IServiceCollection AddProductionRegistrations(IServiceCollection services)
    {
        services.AddDbContext<ApplicationDbContext>(
            options => options.UseInMemoryDatabase("repo-lifetime"),
            ServiceLifetime.Scoped);

        services.AddSingleton<IHitRepository, DbHitRepository>();
        services.AddSingleton<IJobRepository, DbJobRepository>();
        services.AddSingleton<IProxyRepository, DbProxyRepository>();
        return services;
    }

    [Fact]
    public void SingletonRepository_IsSharedBetweenRootAndScope()
    {
        using var provider = AddProductionRegistrations(new ServiceCollection())
            .BuildServiceProvider();

        var fromRoot = provider.GetRequiredService<IHitRepository>();

        using var scope = provider.CreateScope();
        var fromScope = scope.ServiceProvider.GetRequiredService<IHitRepository>();

        // Because the repository is a singleton wrapping a scoped context, a "scoped" resolution
        // still hands back the very same instance (and therefore the same DbContext).
        Assert.Same(fromRoot, fromScope);
    }

    [Fact]
    public void EnablingScopeValidation_RejectsTheCurrentRegistration()
    {
        // This is the guard rail for the eventual fix. While repositories are singletons over a
        // scoped context, the container must not be built with validation enabled. When the
        // repositories are converted to scoped, this test will start failing -- which is the
        // signal to flip App.xaml.cs to use ValidateScopes/ValidateOnBuild and delete this test.
        var services = AddProductionRegistrations(new ServiceCollection());

        var ex = Assert.Throws<AggregateException>(
            () => services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            }));

        Assert.Contains(
            ex.InnerExceptions.OfType<InvalidOperationException>(),
            e => e.Message.Contains("Cannot consume scoped service", StringComparison.Ordinal));
    }

    [Fact]
    public void Repositories_AreResolvableAsSingletons()
    {
        using var provider = AddProductionRegistrations(new ServiceCollection())
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IHitRepository>());
        Assert.NotNull(provider.GetRequiredService<IJobRepository>());
        Assert.NotNull(provider.GetRequiredService<IProxyRepository>());
    }
}
