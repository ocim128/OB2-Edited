using Flux.Native.Enums;
using Flux.Native.Factories;
using Flux.Native.ViewModels;
using Flux.Native.Views.Pages;
using Home = Flux.Native.Views.Pages.Home.Home;
using Jobs = Flux.Native.Views.Pages.Jobs.Jobs;
using Tools = Flux.Native.Views.Pages.Tools.Monitor;
using Proxies = Flux.Native.Views.Pages.Data.Proxies;
using Wordlists = Flux.Native.Views.Pages.Data.Wordlists;
using Hits = Flux.Native.Views.Pages.Data.Hits;
using Plugins = Flux.Native.Views.Pages.Tools.Plugins;
using OBSettings = Flux.Native.Views.Pages.Settings.OBSettings;
using RLSettings = Flux.Native.Views.Pages.Settings.RLSettings;
using About = Flux.Native.Views.Pages.About.About;
using ConfigMetadata = Flux.Native.Views.Pages.Configs.ConfigMetadata;
using ConfigReadme = Flux.Native.Views.Pages.Configs.ConfigReadme;
using ConfigSettings = Flux.Native.Views.Pages.Configs.ConfigSettings;
using ConfigEditor = Flux.Native.Views.Pages.Configs.ConfigEditor;
using ConfigEditorSection = Flux.Native.Views.Pages.Configs.ConfigEditorSection;
using ConfigsPage = Flux.Native.Views.Pages.Configs.Configs;
using Flux.Native.Views.Pages.Shared;
using System;
using System.Collections.Generic;
using System.Windows.Controls;
using Flux.Core.Models.Settings;
using Flux.Core.Services;
using Flux.Native.Helpers;

namespace Flux.Native.Services;

public interface INavigationService
{
    void NavigateTo(MainWindowPage pageEnum);
    void NavigateTo(MainWindowPage pageEnum, object? parameter);
    event EventHandler<NavigationEventArgs>? Navigated;
    Page? CurrentPage { get; }
    MainWindowPage CurrentPageEnum { get; }
}

public class NavigationEventArgs : EventArgs
{
    public Page Page { get; }
    public MainWindowPage PageEnum { get; }

    public NavigationEventArgs(Page page, MainWindowPage pageEnum)
    {
        Page = page;
        PageEnum = pageEnum;
    }
}

public class NavigationService : INavigationService
{
    private readonly IPageFactory _pageFactory;
    private readonly Dictionary<MainWindowPage, Page> _pageCache = new();

    public event EventHandler<NavigationEventArgs>? Navigated;

    public Page? CurrentPage { get; private set; }
    public MainWindowPage CurrentPageEnum { get; private set; } = MainWindowPage.Home; // Default

    public NavigationService(IPageFactory pageFactory)
    {
        _pageFactory = pageFactory;
    }

    public void NavigateTo(MainWindowPage pageEnum)
    {
        NavigateTo(pageEnum, null);
    }

    public void NavigateTo(MainWindowPage pageEnum, object? parameter)
    {
        // Save current page state if it's the ConfigEditor
        if (CurrentPage is ConfigEditor configEditorBefore)
        {
            configEditorBefore.OnPageChanged();
        }

        Page page;

        // Special handling for pages that might need arguments (though currently most are parameterless or handled via ViewModels)
        // or pages we want to re-create every time vs cache.
        // For now, assuming caching for all main pages as per original MainWindow behavior.

        if (_pageCache.TryGetValue(pageEnum, out var cachedPage))
        {
            page = cachedPage;
        }
        else
        {
            page = CreatePage(pageEnum);
            _pageCache[pageEnum] = page;
        }

        // Special logic for ConfigEditor sub-pages (Stacker, LoliCode, etc.)
        // These are actually just setting the ConfigEditor to a specific section.
        if (page is ConfigEditor configEditor && IsConfigEditorPage(pageEnum))
        {
            // We need to tell the ConfigEditor which section to show.
            // This was originally done via helper methods in MainWindow.
            // We can do this by casting logic.
            var section = GetConfigEditorSection(pageEnum);
            configEditor.NavigateTo(section);

            // Ensure we update UI
            configEditor.UpdateUI();
        }

        // Update ViewModel if the page supports it (consistent with original CreateAndNavigateToPage)
        UpdatePageViewModel(page);

        // Perform the navigation
        CurrentPage = page;
        CurrentPageEnum = pageEnum;
        Navigated?.Invoke(this, new NavigationEventArgs(page, pageEnum));
    }

    private Page CreatePage(MainWindowPage pageEnum)
    {
        return pageEnum switch
        {
            MainWindowPage.Home => _pageFactory.CreateHomePage(),
            MainWindowPage.Jobs => _pageFactory.CreateJobsPage(),
            MainWindowPage.Tools => _pageFactory.CreateToolsPage(),
            MainWindowPage.Proxies => _pageFactory.CreateProxiesPage(),
            MainWindowPage.Wordlists => _pageFactory.CreateWordlistsPage(),
            MainWindowPage.Configs => _pageFactory.CreateConfigsPage(),
            MainWindowPage.Hits => _pageFactory.CreateHitsPage(),
            MainWindowPage.Plugins => _pageFactory.CreatePluginsPage(),
            MainWindowPage.OBSettings => _pageFactory.CreateOBSettingsPage(),
            MainWindowPage.RLSettings => _pageFactory.CreateRLSettingsPage(),
            MainWindowPage.About => _pageFactory.CreateAboutPage(),

            // Config Related Pages
            MainWindowPage.ConfigMetadata => _pageFactory.CreateConfigMetadataPage(),
            MainWindowPage.ConfigReadme => _pageFactory.CreateConfigReadmePage(),
            MainWindowPage.ConfigSettings => _pageFactory.CreateConfigSettingsPage(),

            MainWindowPage.ConfigStacker or MainWindowPage.ConfigLoliCode or MainWindowPage.ConfigCSharpCode
                => GetOrCreateSharedConfigEditor(),

            _ => throw new ArgumentException($"Unknown page type: {pageEnum}")
        };
    }

    private ConfigEditor _sharedConfigEditor;
    private ConfigEditor GetOrCreateSharedConfigEditor()
    {
        if (_sharedConfigEditor == null)
        {
            _sharedConfigEditor = _pageFactory.CreateConfigEditorPage();
        }
        return _sharedConfigEditor;
    }

    // Helper to sync cache if we use shared instance
    private void SyncConfigEditorCache(ConfigEditor editor)
    {
        _pageCache[MainWindowPage.ConfigStacker] = editor;
        _pageCache[MainWindowPage.ConfigLoliCode] = editor;
        _pageCache[MainWindowPage.ConfigCSharpCode] = editor;
    }

    private bool IsConfigEditorPage(MainWindowPage page)
    {
        return page == MainWindowPage.ConfigStacker ||
               page == MainWindowPage.ConfigLoliCode ||
               page == MainWindowPage.ConfigCSharpCode;
    }

    private ConfigEditorSection GetConfigEditorSection(MainWindowPage page)
    {
        return page switch
        {
            MainWindowPage.ConfigStacker => ConfigEditorSection.Stacker,
            MainWindowPage.ConfigLoliCode => ConfigEditorSection.LoliCode,
            MainWindowPage.ConfigCSharpCode => ConfigEditorSection.CSharp,
            _ => ConfigEditorSection.Stacker
        };
    }

    private void UpdatePageViewModel(Page page)
    {
        // Reflection-based update as seen in original code, 
        // or we could define an interface IViewModelUpdater { void UpdateViewModel(); } on pages.
        // For now, stick to reflection to avoid touching all page classes.
        var method = page.GetType().GetMethod("UpdateViewModel");
        method?.Invoke(page, null);
    }
}
