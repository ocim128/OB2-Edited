using MahApps.Metro.Controls;
using OpenBullet2.Core.Models.Settings;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.Services.Navigation;
using OpenBullet2.Native.Services.Menu;
using OpenBullet2.Native.Services.Sidebar;
using OpenBullet2.Native.Services.Commands;
using OpenBullet2.Native.Services.Window;
using OpenBullet2.Native.Enums;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.Views.Pages;
using OpenBullet2.Native.Views.Pages.Configs;
using OpenBullet2.Native.Views.Pages.Jobs;
using OpenBullet2.Native.Views.Pages.Data;
using OpenBullet2.Native.Views.Pages.Settings;
using OpenBullet2.Native.Views.Pages.Shared;
using OpenBullet2.Native.Views.Pages.Tools;
using OpenBullet2.Native.ViewModels.Data;
using OpenBullet2.Native.ViewModels.Jobs;
using OpenBullet2.Native.ViewModels.Configs;
using OpenBullet2.Native.ViewModels.Settings;
using OpenBullet2.Native.ViewModels.Tools;
using OpenBullet2.Native.ViewModels.Shared;
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
using Newtonsoft.Json;
using Media = System.Windows.Media;

namespace OpenBullet2.Native;

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
    private readonly OpenBulletSettingsService openBulletSettingsService;
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
        OpenBulletSettingsService openBulletSettingsService,
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
        this.openBulletSettingsService = openBulletSettingsService ?? throw new ArgumentNullException(nameof(openBulletSettingsService));
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
            menuOptionAbout
        ];

        // Lazy initialization - pages created only when needed
        // This reduces initial memory usage and improves startup time

        Title = "OpenBullet 2 - 0.3.3.9 [akunlama MOD]";

        var customization = this.openBulletSettingsService.Settings.CustomizationSettings;
        this.themeService.SetTheme(customization);
        ApplyAccessibilitySettings();
    }

    #region Responsive Design Methods

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize layout service
        windowLayoutService.Initialize(this, Root);
        windowLayoutService.RestoreWindowState();

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
            menuOptionRLSettingsText, menuOptionCheckUpdateText, menuOptionAboutText
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

