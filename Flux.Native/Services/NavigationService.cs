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

    // #COMPLETION_DRIVE: We replaced the unbounded Dictionary page cache with a
    // small LRU (MaxCachedPages entries) that disposes the evicted page's
    // DataContext. The shared ConfigEditor is held in a dedicated field and
    // is never evicted.
    // #SUGGEST_VERIFY: Long-running profile (start job, navigate Home -> Jobs ->
    // Proxies -> Wordlists -> Configs -> Hits -> Plugins) and watch the
    // working set stabilize at ~5 page VMs instead of growing per navigation.
    private const int MaxCachedPages = 4;

    private readonly Dictionary<MainWindowPage, LruEntry> _lruCache = new();
    private readonly LinkedList<MainWindowPage> _lruOrder = new();
    private ConfigEditor? _sharedConfigEditor;

    private readonly record struct LruEntry(Page Page, LinkedListNode<MainWindowPage> Node);

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

        if (IsConfigEditorPage(pageEnum))
        {
            // ConfigEditor sub-pages share a single page instance; the LRU is
            // not used for it because eviction would lose the user's section
            // selection mid-flow.
            page = GetOrCreateSharedConfigEditor();
        }
        else if (_lruCache.TryGetValue(pageEnum, out var cached))
        {
            page = cached.Page;
            TouchLru(pageEnum, cached);
        }
        else
        {
            page = CreatePage(pageEnum);
            InsertIntoLru(pageEnum, page);
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
        if (page is IUpdatablePage updatable)
        {
            updatable.UpdateViewModel();
        }

        // Perform the navigation
        CurrentPage = page;
        CurrentPageEnum = pageEnum;
        Navigated?.Invoke(this, new NavigationEventArgs(page, pageEnum));
    }

    private void TouchLru(MainWindowPage key, LruEntry entry)
    {
        _lruOrder.Remove(entry.Node);
        _lruOrder.AddFirst(entry.Node);
    }

    private void InsertIntoLru(MainWindowPage key, Page page)
    {
        var node = new LinkedListNode<MainWindowPage>(key);
        _lruCache[key] = new LruEntry(page, node);
        _lruOrder.AddFirst(node);

        while (_lruOrder.Count > MaxCachedPages)
        {
            var oldestNode = _lruOrder.Last;
            if (oldestNode == null)
            {
                break;
            }

            _lruOrder.RemoveLast();
            if (_lruCache.Remove(oldestNode.Value, out var evicted))
            {
                DisposePage(evicted.Page);
            }
        }
    }

    private static void DisposePage(Page page)
    {
        try
        {
            if (page.DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch
        {
            // Disposal must never break navigation.
        }
        finally
        {
            // Drop the DataContext reference so the VM (and any singletons
            // it might be subscribing to) can be collected promptly.
            page.DataContext = null;
        }
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

    private ConfigEditor GetOrCreateSharedConfigEditor()
    {
        if (_sharedConfigEditor == null)
        {
            _sharedConfigEditor = _pageFactory.CreateConfigEditorPage();
        }
        return _sharedConfigEditor;
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

}
