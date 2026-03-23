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
        => ActivatorUtilities.CreateInstance<MultiRunJobOptionsViewModel>(serviceProvider, options);
}

public sealed class ProxyCheckJobOptionsViewModelFactory : IProxyCheckJobOptionsViewModelFactory
{
    private readonly IServiceProvider serviceProvider;

    public ProxyCheckJobOptionsViewModelFactory(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public ProxyCheckJobOptionsViewModel Create(ProxyCheckJobOptions? options = null)
        => ActivatorUtilities.CreateInstance<ProxyCheckJobOptionsViewModel>(serviceProvider, options);
}

public sealed class JobOptionsDialogFactory : IJobOptionsDialogFactory
{
    private readonly IServiceProvider serviceProvider;

    public JobOptionsDialogFactory(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public MultiRunJobOptionsDialog CreateMultiRun(MultiRunJobOptions? options = null, Action<JobOptions>? onAccept = null)
        => ActivatorUtilities.CreateInstance<MultiRunJobOptionsDialog>(serviceProvider, options, onAccept);

    public ProxyCheckJobOptionsDialog CreateProxyCheck(ProxyCheckJobOptions? options = null, Action<JobOptions>? onAccept = null)
        => ActivatorUtilities.CreateInstance<ProxyCheckJobOptionsDialog>(serviceProvider, options, onAccept);
}
