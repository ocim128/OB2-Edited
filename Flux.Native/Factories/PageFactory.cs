using System;
using Flux.Native.Views.Pages.Configs;
using Flux.Native.Views.Pages.Data;
using Flux.Native.Views.Pages.Jobs;
using Flux.Native.Views.Pages.Settings;
using Flux.Native.Views.Pages.Shared;
using Flux.Native.Views.Pages.Tools;
using HomePage = Flux.Native.Views.Pages.Home.Home;
using AboutPage = Flux.Native.Views.Pages.About.About;
using JobsPage = Flux.Native.Views.Pages.Jobs.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace Flux.Native.Factories;

public interface IPageFactory
{
    HomePage CreateHomePage();
    JobsPage CreateJobsPage();
    Monitor CreateToolsPage();
    Proxies CreateProxiesPage();
    Wordlists CreateWordlistsPage();
    Configs CreateConfigsPage();
    Hits CreateHitsPage();
    Plugins CreatePluginsPage();
    OBSettings CreateOBSettingsPage();
    RLSettings CreateRLSettingsPage();
    AboutPage CreateAboutPage();
    ConfigMetadata CreateConfigMetadataPage();
    ConfigReadme CreateConfigReadmePage();
    ConfigSettings CreateConfigSettingsPage();
    ConfigEditor CreateConfigEditorPage();
    ConfigStacker CreateConfigStackerPage();
    ConfigLoliCode CreateConfigLoliCodePage();
    ConfigCSharpCode CreateConfigCSharpCodePage();
    Debugger CreateDebuggerPage();
    MultiRunJobViewer CreateMultiRunJobViewerPage();
    ProxyCheckJobViewer CreateProxyCheckJobViewerPage();
}

public sealed class PageFactory : IPageFactory
{
    private readonly IServiceProvider serviceProvider;

    public PageFactory(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public HomePage CreateHomePage() => ActivatorUtilities.CreateInstance<HomePage>(serviceProvider);
    public JobsPage CreateJobsPage() => ActivatorUtilities.CreateInstance<JobsPage>(serviceProvider);
    public Monitor CreateToolsPage() => ActivatorUtilities.CreateInstance<Monitor>(serviceProvider);
    public Proxies CreateProxiesPage() => ActivatorUtilities.CreateInstance<Proxies>(serviceProvider);
    public Wordlists CreateWordlistsPage() => ActivatorUtilities.CreateInstance<Wordlists>(serviceProvider);
    public Configs CreateConfigsPage() => ActivatorUtilities.CreateInstance<Configs>(serviceProvider);
    public Hits CreateHitsPage() => ActivatorUtilities.CreateInstance<Hits>(serviceProvider);
    public Plugins CreatePluginsPage() => ActivatorUtilities.CreateInstance<Plugins>(serviceProvider);
    public OBSettings CreateOBSettingsPage() => ActivatorUtilities.CreateInstance<OBSettings>(serviceProvider);
    public RLSettings CreateRLSettingsPage() => ActivatorUtilities.CreateInstance<RLSettings>(serviceProvider);
    public AboutPage CreateAboutPage() => ActivatorUtilities.CreateInstance<AboutPage>(serviceProvider);
    public ConfigMetadata CreateConfigMetadataPage() => ActivatorUtilities.CreateInstance<ConfigMetadata>(serviceProvider);
    public ConfigReadme CreateConfigReadmePage() => ActivatorUtilities.CreateInstance<ConfigReadme>(serviceProvider);
    public ConfigSettings CreateConfigSettingsPage() => ActivatorUtilities.CreateInstance<ConfigSettings>(serviceProvider);
    public ConfigEditor CreateConfigEditorPage() => ActivatorUtilities.CreateInstance<ConfigEditor>(serviceProvider);
    public ConfigStacker CreateConfigStackerPage() => ActivatorUtilities.CreateInstance<ConfigStacker>(serviceProvider);
    public ConfigLoliCode CreateConfigLoliCodePage() => ActivatorUtilities.CreateInstance<ConfigLoliCode>(serviceProvider);
    public ConfigCSharpCode CreateConfigCSharpCodePage() => ActivatorUtilities.CreateInstance<ConfigCSharpCode>(serviceProvider);
    public Debugger CreateDebuggerPage() => ActivatorUtilities.CreateInstance<Debugger>(serviceProvider);
    public MultiRunJobViewer CreateMultiRunJobViewerPage() => ActivatorUtilities.CreateInstance<MultiRunJobViewer>(serviceProvider);
    public ProxyCheckJobViewer CreateProxyCheckJobViewerPage() => ActivatorUtilities.CreateInstance<ProxyCheckJobViewer>(serviceProvider);
}
