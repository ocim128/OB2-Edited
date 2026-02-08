using MahApps.Metro.Controls;
using Flux.Core.Models.Settings;
using Flux.Core.Repositories;
using Flux.Core.Services;
using Flux.Native.Helpers;
using Flux.Native.Services;
using Flux.Native.Services.Navigation;
using Flux.Native.Services.Menu;
using Flux.Native.Services.Sidebar;
using Flux.Native.Services.Commands;
using Flux.Native.Services.Window;
using Flux.Native.Enums;
using Flux.Native.ViewModels;
using Flux.Native.Views.Pages;
using Flux.Native.Views.Pages.Configs;
using Flux.Native.Views.Pages.Jobs;
using Flux.Native.Views.Pages.Data;
using Flux.Native.Views.Pages.Settings;
using Flux.Native.Views.Pages.Shared;
using Flux.Native.Views.Pages.Tools;
using Flux.Native.ViewModels.Data;
using Flux.Native.ViewModels.Jobs;
using Flux.Native.ViewModels.Configs;
using Flux.Native.ViewModels.Settings;
using Flux.Native.ViewModels.Tools;
using Flux.Native.ViewModels.Shared;
using RuriLib.Models.Configs;
using RuriLib.Models.Jobs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Newtonsoft.Json;
using Media = System.Windows.Media;

namespace Flux.Native;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : MetroWindow
{
    private readonly MainWindowViewModel vm;

    private Button currentSelectedButton; // Track current selected button for optimization
    private bool isSidebarCollapsed = true; // Track sidebar collapsed state - starts collapsed

    private readonly Button[] labels;
    private readonly Button[] navigationButtons; // Modern navigation buttons array

    private readonly HotkeyService hotkeyService;
    private readonly FluxSettingsService fluxSettingsService;
    private readonly IAppUpdateService appUpdateService;
    private readonly IWindowLayoutService windowLayoutService;
    private readonly IThemeService themeService;
    private readonly INavigationHandler navigationHandler;
    private readonly IMenuHandler menuHandler;
    private readonly ISidebarHandler sidebarHandler;
    private readonly ICommandHandler commandHandler;
    private readonly IWindowControlHandler windowControlHandler;
    private readonly AccessibilityHandler accessibilityHandler;

    public System.Windows.Controls.Page CurrentPage { get; private set; }

    // ConfigEditor property for accessing shared instance if needed
    public ConfigEditor ConfigEditorPage => navigationHandler.CurrentPage as ConfigEditor;

    /// <summary>
    /// Responsive design properties
    /// </summary>

    public MainWindow(
        MainWindowViewModel viewModel,
        HotkeyService hotkeyService,
        FluxSettingsService fluxSettingsService,
        IAppUpdateService appUpdateService,
        IWindowLayoutService windowLayoutService,
        IThemeService themeService,
        INavigationHandler navigationHandler,
        IMenuHandler menuHandler,
        ISidebarHandler sidebarHandler,
        ICommandHandler commandHandler,
        IWindowControlHandler windowControlHandler,
        AccessibilityHandler accessibilityHandler)
    {
        vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
        this.fluxSettingsService = fluxSettingsService ?? throw new ArgumentNullException(nameof(fluxSettingsService));
        this.appUpdateService = appUpdateService ?? throw new ArgumentNullException(nameof(appUpdateService));
        this.windowLayoutService = windowLayoutService ?? throw new ArgumentNullException(nameof(windowLayoutService));
        this.themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        this.navigationHandler = navigationHandler ?? throw new ArgumentNullException(nameof(navigationHandler));
        this.menuHandler = menuHandler ?? throw new ArgumentNullException(nameof(menuHandler));
        this.sidebarHandler = sidebarHandler ?? throw new ArgumentNullException(nameof(sidebarHandler));
        this.commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
        this.windowControlHandler = windowControlHandler ?? throw new ArgumentNullException(nameof(windowControlHandler));
        this.accessibilityHandler = accessibilityHandler ?? throw new ArgumentNullException(nameof(accessibilityHandler));

        this.windowControlHandler.SetWindow(this);

        this.themeService.Initialize(this);

        this.navigationHandler.Navigated += OnNavigated;

        DataContext = vm;
        Closing += vm.OnWindowClosing;

        InitializeComponent();

        Loaded += OnWindowLoaded;
        StateChanged += windowControlHandler.OnWindowStateChanged;
        this.hotkeyService.Initialize(this);

        // Command Bindings
        this.commandHandler.InitializeCommandBindings(this);

        labels =
        [
            menuOptionAbout,
            menuOptionCheckUpdate,
            menuOptionThemeToggle,
            menuOptionConfigs,
            menuOptionConfigSettings,
            menuOptionCSharpCode,
            menuOptionHits,
            menuOptionHome,
            menuOptionJobs,
            menuOptionLoliCode,
            menuOptionMetadata,
            menuOptionTools,
            menuOptionRLSettings,
            menuOptionSettings,
            menuOptionPlugins,
            menuOptionProxies,
            menuOptionReadme,
            menuOptionStacker,
            menuOptionWordlists
        ];

        // Initialize navigation buttons array for modern menu
        navigationButtons =
        [
            menuOptionHome,
            menuOptionJobs,
            menuOptionTools,
            menuOptionProxies,
            menuOptionWordlists,
            menuOptionConfigs,
            menuOptionHits,
            menuOptionPlugins,
            menuOptionSettings,
            menuOptionRLSettings,
            menuOptionCheckUpdate,
            menuOptionAbout,
            menuOptionThemeToggle
        ];

        // Lazy initialization - pages created only when needed
        // This reduces initial memory usage and improves startup time

        Title = "Flux - 0.3.3.9 [akunlama MOD]";

        var customization = this.fluxSettingsService.Settings.CustomizationSettings;
        this.themeService.SetTheme(customization);
        UpdateThemeToggleUi();
        ApplyAccessibilitySettings();
    }

    #region Responsive Design Methods

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize layout service
        windowLayoutService.Initialize(this, Root);
        windowLayoutService.RestoreWindowState();
        
        // Re-apply theme after window chrome/state is finalized so caption buttons
        // pick the correct light/dark visuals on first render.
        var customization = fluxSettingsService.Settings.CustomizationSettings;
        themeService.SetTheme(customization);
        UpdateThemeToggleUi();

        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => themeService.SetTheme(customization)));

        // Initialize button map dynamically
        InitializePageButtonMap();
        menuHandler.Initialize(configSubmenu, ConfigsChevron);

        // Initialize sidebar handler with UI elements
        InitializeSidebar();

        // Initialize sidebar in collapsed state
        sidebarHandler.InitializeCollapsedState();
    }

    private void InitializeSidebar()
    {
        var textElements = new FrameworkElement[]
        {
            menuOptionHomeText, menuOptionJobsText, menuOptionHitsText,
            menuOptionConfigsText, menuOptionWordlistsText, menuOptionProxiesText,
            menuOptionToolsText, menuOptionPluginsText, menuOptionSettingsText,
            menuOptionRLSettingsText, menuOptionCheckUpdateText, menuOptionAboutText,
            menuOptionThemeToggleText
        };

        var sectionHeaders = new FrameworkElement[]
        {
            SectionMain, SectionResources, SectionSystem
        };

        sidebarHandler.Initialize(
            SidebarColumn,
            ToggleIconRotation,
            textElements,
            sectionHeaders,
            SidebarHeader,
            VersionText,
            BottomSeparator,
            configSubmenu,
            ConfigsChevron);
    }

    #endregion Responsive Design Methods

    public Task NavigateTo(MainWindowPage page)
    {
        return navigationHandler.NavigateTo(page);
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        CurrentPage = e.Page;
        MainFrame.Content = e.Page;

        vm.IsLoading = false;
    }

    private void InitializePageButtonMap()
    {
        // Actually, we can just pass the array to the handler if it knows the order
        // but for safety, let's map them explicitly here for now
        menuHandler.MapButton(MainWindowPage.Home, menuOptionHome);
        menuHandler.MapButton(MainWindowPage.Jobs, menuOptionJobs);
        menuHandler.MapButton(MainWindowPage.Tools, menuOptionTools);
        menuHandler.MapButton(MainWindowPage.Proxies, menuOptionProxies);
        menuHandler.MapButton(MainWindowPage.Wordlists, menuOptionWordlists);
        menuHandler.MapButton(MainWindowPage.Configs, menuOptionConfigs);
        menuHandler.MapButton(MainWindowPage.Hits, menuOptionHits);
        menuHandler.MapButton(MainWindowPage.Plugins, menuOptionPlugins);
        menuHandler.MapButton(MainWindowPage.OBSettings, menuOptionSettings);
        menuHandler.MapButton(MainWindowPage.RLSettings, menuOptionRLSettings);
        menuHandler.MapButton(MainWindowPage.CheckUpdate, menuOptionCheckUpdate);
        menuHandler.MapButton(MainWindowPage.About, menuOptionAbout);

        // Map config submenu buttons
        menuHandler.MapButton(MainWindowPage.ConfigMetadata, menuOptionMetadata);
        menuHandler.MapButton(MainWindowPage.ConfigReadme, menuOptionReadme);
        menuHandler.MapButton(MainWindowPage.ConfigStacker, menuOptionStacker);
        menuHandler.MapButton(MainWindowPage.ConfigLoliCode, menuOptionLoliCode);
        menuHandler.MapButton(MainWindowPage.ConfigSettings, menuOptionConfigSettings);
        menuHandler.MapButton(MainWindowPage.ConfigCSharpCode, menuOptionCSharpCode);
    }

    public void DisplayJob(JobViewModel jobVM)
    {
        navigationHandler.DisplayJob(jobVM);
    }

    public void EditJob(JobViewModel jobVM)
    {
        navigationHandler.EditJob(jobVM);
    }

    // Modern navigation handler for button clicks
    private async void HandleNavigationClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;

        // Handle update check separately
        if (button?.Name == "menuOptionCheckUpdate")
        {
            await appUpdateService.CheckForUpdatesAsync();
            return;
        }

        // Use Tag property for navigation mapping - more reliable for buttons
        if (button?.Tag != null && Enum.TryParse<MainWindowPage>(button.Tag.ToString(), out var targetPage))
        {
            await NavigateTo(targetPage);
        }
        else
        {
            // Fallback mapping via our helper if Tag fails or is missing
            // We can't easily reverse GetButtonForPage without a map.
            // But we can check button names.
            // Or just assume if Tag is missing it might be mapped by name if we support it.
            // Simplified:
            if (button != null)
            {
                // Try to find which page this button corresponds to
                // Iterate Enum?
                foreach (MainWindowPage page in Enum.GetValues(typeof(MainWindowPage)))
                {
                    if (menuHandler.GetButtonForPage(page) == button)
                    {
                        await NavigateTo(page);
                        return;
                    }
                }
            }

            await NavigateTo(MainWindowPage.Home);
        }
    }





    private void MinimizeWindow(object sender, RoutedEventArgs e) => windowControlHandler.Minimize();

    private void MaximizeRestoreWindow(object sender, RoutedEventArgs e) => windowControlHandler.MaximizeRestore();

    private void CloseWindow(object sender, RoutedEventArgs e) => windowControlHandler.Close();

    #region Sidebar Toggle Logic
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        sidebarHandler.Toggle();
    }

    private async void ToggleThemeMode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var customization = fluxSettingsService.Settings.CustomizationSettings;
            var isDark = string.Equals(customization.NativeThemeMode, "Dark", StringComparison.OrdinalIgnoreCase);
            customization.NativeThemeMode = isDark ? "Light" : "Dark";

            themeService.SetTheme(customization);
            UpdateThemeToggleUi();

            await fluxSettingsService.SaveAsync();
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }

    private void UpdateThemeToggleUi()
    {
        if (menuOptionThemeToggleText is null || menuOptionThemeToggle is null)
        {
            return;
        }

        var isDark = string.Equals(
            fluxSettingsService.Settings.CustomizationSettings.NativeThemeMode,
            "Dark",
            StringComparison.OrdinalIgnoreCase);

        menuOptionThemeToggleText.Text = isDark ? "Theme: Dark" : "Theme: Light";
        menuOptionThemeToggle.ToolTip = isDark ? "Switch to Light Mode" : "Switch to Dark Mode";
    }

    public void ToggleSidebar()
    {
        sidebarHandler.Toggle();
    }
    #endregion Sidebar Toggle Logic

    #region Dropdown submenu logic
    private async void ConfigSubmenuMouseEnter(object sender, MouseEventArgs e)
    {
        if (vm.IsConfigSelected)
            await menuHandler.OnConfigSubmenuMouseEnterAsync();
    }

    private async void ConfigSubmenuMouseLeave(object sender, MouseEventArgs e)
    {
        await menuHandler.OnConfigSubmenuMouseLeaveAsync();
    }

    private async void ConfigsMenuOptionMouseEnter(object sender, MouseEventArgs e)
    {
        if (vm.IsConfigSelected)
            await menuHandler.OnConfigsMenuOptionMouseEnterAsync();
    }

    private async void ConfigsMenuOptionMouseLeave(object sender, MouseEventArgs e)
    {
        await menuHandler.OnConfigsMenuOptionMouseLeaveAsync();
    }

    private void CloseSubmenu() => menuHandler.CloseSubmenu();
    #endregion Dropdown submenu logic

    private void ApplyAccessibilitySettings()
    {
        var submenuButtons = new[]
        {
            menuOptionMetadata,
            menuOptionReadme,
            menuOptionStacker,
            menuOptionLoliCode,
            menuOptionConfigSettings,
            menuOptionCSharpCode
        };

        accessibilityHandler.ApplyAccessibilitySettings(this, navigationButtons, submenuButtons, configSubmenu);
    }

}

