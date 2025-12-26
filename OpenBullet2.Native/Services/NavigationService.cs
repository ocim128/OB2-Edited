using OpenBullet2.Native.Enums;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.Views.Pages;
using System;
using System.Collections.Generic;
using System.Windows.Controls;
using OpenBullet2.Core.Models.Settings;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Views.Pages.Shared; // For Debugger if needed
using Microsoft.Extensions.DependencyInjection;

namespace OpenBullet2.Native.Services;

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
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<MainWindowPage, Page> _pageCache = new();
    
    public event EventHandler<NavigationEventArgs>? Navigated;
    
    public Page? CurrentPage { get; private set; }
    public MainWindowPage CurrentPageEnum { get; private set; } = MainWindowPage.Home; // Default

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
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
        switch (pageEnum)
        {
            case MainWindowPage.Home: return new Home();
            case MainWindowPage.Jobs: return new Jobs(); // Jobs page was handled specifically, but a new instance seems fine if cached.
            case MainWindowPage.Tools: return new Tools();
            case MainWindowPage.Proxies: return new Proxies();
            case MainWindowPage.Wordlists: return new Wordlists();
            case MainWindowPage.Configs: return new Configs();
            case MainWindowPage.Hits: return new Hits();
            case MainWindowPage.Plugins: return new Plugins();
            case MainWindowPage.OBSettings: return new OBSettings();
            case MainWindowPage.RLSettings: return new RLSettings();
            case MainWindowPage.About: return new About(); // About was cached
            
            // Config Related Pages
            case MainWindowPage.ConfigMetadata: return new Views.Pages.ConfigMetadata();
            case MainWindowPage.ConfigReadme: return new ConfigReadme();
            case MainWindowPage.ConfigSettings: return new Views.Pages.ConfigSettings();
            
            // These all share the ConfigEditor page instance
            case MainWindowPage.ConfigStacker:
            case MainWindowPage.ConfigLoliCode:
            case MainWindowPage.ConfigCSharpCode:
                // Check if we already have a ConfigEditor instance cached under any of these keys
                // Actually, the original code had a single `configEditorPage` field. 
                // So we should probably cache it under one key (e.g. ConfigStacker?) or handle it specially.
                // Let's use a shared instance strategy.
                return GetOrCreateSharedConfigEditor();

            // CheckUpdate is an action, not really a page, handled in MainWindow usually?
            // If it's a page in the enum, we should have a page for it? 
            // In original code: MenuNavigationMap["menuOptionCheckUpdate"] = MainWindowPage.CheckUpdate;
            // But HandleNavigationClick had a special check: if (button?.Name == "menuOptionCheckUpdate")
            // So it might not be a real page. 
            // If NavigateTo is called with CheckUpdate, what should happen? 
            // Probably nothing or return Home if invalid.
            case MainWindowPage.CheckUpdate: 
                throw new InvalidOperationException("CheckUpdate is an action, not a navigable page.");

            default:
                throw new ArgumentException($"Unknown page type: {pageEnum}");
        }
    }

    private ConfigEditor _sharedConfigEditor;
    private ConfigEditor GetOrCreateSharedConfigEditor()
    {
        if (_sharedConfigEditor == null)
        {
            _sharedConfigEditor = new ConfigEditor();
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
