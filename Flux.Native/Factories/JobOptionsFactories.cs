using System;
using Flux.Core.Models.Jobs;
using Flux.Native.ViewModels.Jobs;
using Flux.Native.Views.Dialogs.Job;
using Microsoft.Extensions.DependencyInjection;
using RuriLib.Models.Jobs;

namespace Flux.Native.Factories;

public interface IMultiRunJobOptionsViewModelFactory
{
    MultiRunJobOptionsViewModel Create(MultiRunJobOptions? options = null);
}

public interface IProxyCheckJobOptionsViewModelFactory
{
    ProxyCheckJobOptionsViewModel Create(ProxyCheckJobOptions? options = null);
}

public interface IJobOptionsDialogFactory
{
    MultiRunJobOptionsDialog CreateMultiRun(MultiRunJobOptions? options = null, Action<JobOptions>? onAccept = null);
    ProxyCheckJobOptionsDialog CreateProxyCheck(ProxyCheckJobOptions? options = null, Action<JobOptions>? onAccept = null);
}

public sealed class MultiRunJobOptionsViewModelFactory : IMultiRunJobOptionsViewModelFactory
{
    private readonly IServiceProvider serviceProvider;

    public MultiRunJobOptionsViewModelFactory(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public MultiRunJobOptionsViewModel Create(MultiRunJobOptions? options = null)
    {
        return new MultiRunJobOptionsViewModel(
            options,
            serviceProvider.GetRequiredService<Flux.Core.Repositories.IRecordRepository>(),
            serviceProvider.GetRequiredService<Flux.Core.Repositories.IWordlistRepository>(),
            serviceProvider.GetRequiredService<RuriLib.Services.RuriLibSettingsService>(),
            serviceProvider.GetRequiredService<Flux.Core.Services.ConfigService>(),
            serviceProvider.GetRequiredService<Flux.Core.Services.JobFactoryService>(),
            serviceProvider.GetRequiredService<Flux.Core.Repositories.IProxyGroupRepository>()
        );
    }
}

public sealed class ProxyCheckJobOptionsViewModelFactory : IProxyCheckJobOptionsViewModelFactory
{
    private readonly IServiceProvider serviceProvider;

    public ProxyCheckJobOptionsViewModelFactory(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public ProxyCheckJobOptionsViewModel Create(ProxyCheckJobOptions? options = null)
    {
        return new ProxyCheckJobOptionsViewModel(
            options,
            serviceProvider.GetRequiredService<Flux.Core.Repositories.IProxyGroupRepository>(),
            serviceProvider.GetRequiredService<Flux.Core.Services.JobFactoryService>(),
            serviceProvider.GetRequiredService<Flux.Core.Services.FluxSettingsService>()
        );
    }
}

public sealed class JobOptionsDialogFactory : IJobOptionsDialogFactory
{
    private readonly IMultiRunJobOptionsViewModelFactory multiRunFactory;
    private readonly IProxyCheckJobOptionsViewModelFactory proxyCheckFactory;

    public JobOptionsDialogFactory(
        IMultiRunJobOptionsViewModelFactory multiRunFactory,
        IProxyCheckJobOptionsViewModelFactory proxyCheckFactory)
    {
        this.multiRunFactory = multiRunFactory;
        this.proxyCheckFactory = proxyCheckFactory;
    }

    public MultiRunJobOptionsDialog CreateMultiRun(MultiRunJobOptions? options = null, Action<JobOptions>? onAccept = null)
        => new MultiRunJobOptionsDialog(multiRunFactory, options, onAccept);

    public ProxyCheckJobOptionsDialog CreateProxyCheck(ProxyCheckJobOptions? options = null, Action<JobOptions>? onAccept = null)
        => new ProxyCheckJobOptionsDialog(proxyCheckFactory, options, onAccept);
}
