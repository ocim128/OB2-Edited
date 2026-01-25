using MahApps.Metro.Controls;
using OpenBullet2.Core.Models.Settings;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
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

    private bool hoveringConfigsMenuOption;
    private bool hoveringConfigSubmenu;
    private Button currentSelectedButton; // Track current selected button for optimization
    private bool isSidebarCollapsed = true; // Track sidebar collapsed state - starts collapsed

    private readonly Button[] labels;
    private readonly Button[] navigationButtons; // Modern navigation buttons array

    private readonly HotkeyService hotkeyService;
    private readonly OpenBulletSettingsService openBulletSettingsService;
    private readonly ConfigService configService;
    private readonly IConfigRepository configRepository;
    private readonly IAppUpdateService appUpdateService;
    private readonly INavigationService navigationService;
    private readonly IWindowLayoutService windowLayoutService;
    private readonly IThemeService themeService;
    private readonly Dictionary<MainWindowPage, Button> pageButtonMap = new();

    private AccessibilitySettings AccessibilitySettings => openBulletSettingsService.Settings.AccessibilitySettings ?? new AccessibilitySettings();

    // Pages are now managed by NavigationService
    public Page CurrentPage { get; private set; }

    // ConfigEditor property for accessing shared instance if needed (e.g. by ViewModelsService or similar?)
    // Originally exposed for some reason. Let's redirect to NavigationService or Cast current page.
    public ConfigEditor ConfigEditorPage => navigationService.CurrentPage as ConfigEditor;

    /// <summary>
    /// Responsive design properties
    /// </summary>

    public MainWindow(
        MainWindowViewModel viewModel,
        HotkeyService hotkeyService,
        OpenBulletSettingsService openBulletSettingsService,
        ConfigService configService,
        IConfigRepository configRepository,
        IAppUpdateService appUpdateService,
        INavigationService navigationService,
        IWindowLayoutService windowLayoutService,
        IThemeService themeService)
    {
        vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
        this.openBulletSettingsService = openBulletSettingsService ?? throw new ArgumentNullException(nameof(openBulletSettingsService));
        this.configService = configService ?? throw new ArgumentNullException(nameof(configService));
        this.configRepository = configRepository ?? throw new ArgumentNullException(nameof(configRepository));
        this.appUpdateService = appUpdateService ?? throw new ArgumentNullException(nameof(appUpdateService));
        this.navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        this.windowLayoutService = windowLayoutService ?? throw new ArgumentNullException(nameof(windowLayoutService));
        this.themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));

        this.themeService.Initialize(this);

        this.navigationService.Navigated += OnNavigated;

        DataContext = vm;
        Closing += vm.OnWindowClosing;

        InitializeComponent();

        Loaded += OnWindowLoaded;
        StateChanged += OnWindowStateChanged;

        // Command Bindings
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.NewConfig, OnNewConfigExecuted, OnCanExecuteConfigCommand));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.OpenConfig, OnOpenConfigExecuted, OnCanExecuteConfigCommand));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.SaveConfig, OnSaveConfigExecuted, OnCanExecuteConfigCommand));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.Refresh, OnRefreshExecuted, OnCanExecuteRefreshCommand));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.Quit, OnQuitExecuted));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.ToggleSidebar, (s, e) => ToggleSidebar()));

        // Navigation Commands
        BindNavigationCommand(CustomCommands.NavigateToHome, MainWindowPage.Home);
        BindNavigationCommand(CustomCommands.NavigateToJobs, MainWindowPage.Jobs);
        BindNavigationCommand(CustomCommands.NavigateToTools, MainWindowPage.Tools);
        BindNavigationCommand(CustomCommands.NavigateToProxies, MainWindowPage.Proxies);
        BindNavigationCommand(CustomCommands.NavigateToWordlists, MainWindowPage.Wordlists);
        BindNavigationCommand(CustomCommands.NavigateToConfigs, MainWindowPage.Configs);
        BindNavigationCommand(CustomCommands.NavigateToHits, MainWindowPage.Hits);
        BindNavigationCommand(CustomCommands.NavigateToPlugins, MainWindowPage.Plugins);
        BindNavigationCommand(CustomCommands.NavigateToOBSettings, MainWindowPage.OBSettings);
        BindNavigationCommand(CustomCommands.NavigateToRLSettings, MainWindowPage.RLSettings);

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

        this.hotkeyService.Initialize(this);

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

        // Initialize sidebar in collapsed state
        InitializeSidebarState();
    }

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
        NotifyDebuggerWindowStateChanged(WindowState == WindowState.Minimized);
    }

    private void NotifyDebuggerWindowStateChanged(bool isMinimized)
    {
        try
        {
            // Check if we're on a config editor page that contains the debugger
            if (CurrentPage is ConfigEditor editor && editor.debuggerFrame?.Content is Views.Pages.Shared.Debugger debugger)
            {
                // Directly access the debugger from the debuggerFrame
                debugger.SetWindowMinimized(isMinimized);
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash the application
            System.Diagnostics.Debug.WriteLine($"Error notifying debugger of window state change: {ex.Message}");
        }
    }







    #endregion Responsive Design Methods

    public Task NavigateTo(MainWindowPage page)
    {
        vm.IsLoading = true;
        navigationService.NavigateTo(page);
        return Task.CompletedTask;
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        CurrentPage = e.Page;
        MainFrame.Content = e.Page;

        UpdateMenuHighlight(e.PageEnum);

        vm.IsLoading = false;
    }

    private void UpdateMenuHighlight(MainWindowPage page)
    {
        var button = GetButtonForPage(page);

        if (button == currentSelectedButton)
            return;

        // Revert previous button to normal style
        if (currentSelectedButton != null)
        {
            currentSelectedButton.Style = currentSelectedButton.Name.StartsWith("menuOptionConfig")
                ? FindResource("SidebarSubmenuButton") as Style
                : FindResource("SidebarNavButton") as Style;
        }

        // Apply active style to new button
        if (button != null)
        {
            button.Style = FindResource("SidebarNavButtonActive") as Style;
            currentSelectedButton = button;
        }
    }

    private void InitializePageButtonMap()
    {
        // Automatically map standard buttons
        MapButton(MainWindowPage.Home, menuOptionHome);
        MapButton(MainWindowPage.Jobs, menuOptionJobs);
        MapButton(MainWindowPage.Tools, menuOptionTools);
        MapButton(MainWindowPage.Proxies, menuOptionProxies);
        MapButton(MainWindowPage.Wordlists, menuOptionWordlists);
        MapButton(MainWindowPage.Configs, menuOptionConfigs);
        MapButton(MainWindowPage.Hits, menuOptionHits);
        MapButton(MainWindowPage.Plugins, menuOptionPlugins);
        MapButton(MainWindowPage.OBSettings, menuOptionSettings);
        MapButton(MainWindowPage.RLSettings, menuOptionRLSettings);
        MapButton(MainWindowPage.CheckUpdate, menuOptionCheckUpdate);
        MapButton(MainWindowPage.About, menuOptionAbout);

        // Map config submenu buttons
        MapButton(MainWindowPage.ConfigMetadata, menuOptionMetadata);
        MapButton(MainWindowPage.ConfigReadme, menuOptionReadme);
        MapButton(MainWindowPage.ConfigStacker, menuOptionStacker);
        MapButton(MainWindowPage.ConfigLoliCode, menuOptionLoliCode);
        MapButton(MainWindowPage.ConfigSettings, menuOptionConfigSettings);
        MapButton(MainWindowPage.ConfigCSharpCode, menuOptionCSharpCode);
    }

    private void MapButton(MainWindowPage page, Button button)
    {
        if (button != null)
        {
            pageButtonMap[page] = button;
            // Ensure Tag is set for reverse lookup
            if (button.Tag == null)
            {
                button.Tag = page;
            }
        }
    }

    private Button GetButtonForPage(MainWindowPage page)
    {
        return pageButtonMap.TryGetValue(page, out var button) ? button : null;
    }



    public void DisplayJob(JobViewModel jobVM)
    {
        // For job display, we might need a way to navigate to job viewer via service or just set content manually if it's transient.
        // Or add JobViewer pages to NavigationService?
        // NavigationService handles main pages. JobViewer is a sub-page or variant.
        // For now, let's keep it manual or use service if we create a JobViewer page type.
        // But NavigationService takes an Enum.
        // Let's create the page and set it manually, updating CurrentPage.
        // This bypasses NavigationService cache which might be desired for transient job views.

        switch (jobVM)
        {
            case MultiRunJobViewModel mrj:
                var mrjPage = new MultiRunJobViewer();
                mrjPage.BindViewModel(mrj);
                ChangePage(mrjPage, null);
                break;

            case ProxyCheckJobViewModel pcj:
                var pcjPage = new ProxyCheckJobViewer();
                pcjPage.BindViewModel(pcj);
                ChangePage(pcjPage, null);
                break;
        }
    }

    public void EditJob(JobViewModel jobVM)
    {
        NavigateTo(MainWindowPage.Jobs);
        // Note: This relies on NavigateTo(Jobs) to set the page. 
        // Then we get the page instance and call EditJob.
        // Since we didn't expose GetPage from Service, we can rely on CurrentPage after navigation...
        // But NavigateTo is async waiting for Navigated probably?
        // My implementation fires Navigated synchronously.
        if (navigationService.CurrentPage is Jobs initialJobsPage) // Use NavigationService.CurrentPage
        {
            initialJobsPage.EditJob(jobVM);
        }
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
                    if (GetButtonForPage(page) == button)
                    {
                        await NavigateTo(page);
                        return;
                    }
                }
            }

            await NavigateTo(MainWindowPage.Home);
        }
    }

    // Helper for transient pages (Job Viewing) that aren't in the main navigation enum
    private void ChangePage(Page newPage, Button newButton)
    {
        CurrentPage = newPage;
        MainFrame.Content = newPage;

        // Update indicators
        if (currentSelectedButton != null)
        {
            // Revert previous
            if (currentSelectedButton.Name.StartsWith("menuOptionConfig"))
                currentSelectedButton.Style = FindResource("SidebarSubmenuButton") as Style;
            else
                currentSelectedButton.Style = FindResource("SidebarNavButton") as Style;

            currentSelectedButton = null;
        }

        if (newButton != null)
        {
            newButton.Style = FindResource("SidebarNavButtonActive") as Style;
            currentSelectedButton = newButton;
        }

        vm.IsLoading = false;
    }

    private void OnCanExecuteConfigCommand(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = navigationService.CurrentPageEnum is
            MainWindowPage.Configs or
            MainWindowPage.ConfigStacker or
            MainWindowPage.ConfigLoliCode or
            MainWindowPage.ConfigCSharpCode or
            MainWindowPage.ConfigMetadata or
            MainWindowPage.ConfigReadme or
            MainWindowPage.ConfigSettings;

    private void OnNewConfigExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (CurrentPage is Configs page)
            page.Create(null, null);
    }

    private void OnOpenConfigExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (CurrentPage is Configs page)
            page.Edit(null, null);
    }

    private void OnSaveConfigExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (CurrentPage is Configs configs)
        {
            configs.Save(null, null);
            return;
        }

        if (CurrentPage is ConfigEditor editor)
        {
            editor.Save(null, null);
            return;
        }

        // Fallback for other pages
        if (configService.SelectedConfig != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await configRepository.SaveAsync(configService.SelectedConfig);
                    configService.SelectedConfig.UpdateHashes();
                    Application.Current.Dispatcher.Invoke(() => Alert.Success("Saved", $"{configService.SelectedConfig.Metadata.Name} was saved successfully!"));
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() => Alert.Exception(ex));
                }
            });
        }
    }

    private async void OnRefreshExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (CurrentPage is Configs configs)
        {
            configs.Rescan(null, null);
        }
        else if (CurrentPage is Hits hits)
        {
            await hits.Refresh();
        }
        else if (CurrentPage is Proxies proxies)
        {
            await proxies.Refresh();
        }
        else if (CurrentPage is Wordlists wordlists)
        {
            await wordlists.Refresh();
        }
        else if (CurrentPage is Plugins plugins)
        {
            plugins.Refresh();
        }
    }

    private void OnCanExecuteRefreshCommand(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = navigationService.CurrentPageEnum is
            MainWindowPage.Configs or
            MainWindowPage.Hits or
            MainWindowPage.Proxies or
            MainWindowPage.Wordlists or
            MainWindowPage.Plugins;

    private void OnQuitExecuted(object sender, ExecutedRoutedEventArgs e) => Application.Current.Shutdown();

    private void BindNavigationCommand(ICommand command, MainWindowPage page)
    {
        _ = CommandBindings.Add(new CommandBinding(command, (s, e) => NavigateTo(page)));
    }



    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestoreWindow(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    #region Sidebar Toggle Logic
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidebar();
    }

    private void ToggleSidebar()
    {
        isSidebarCollapsed = !isSidebarCollapsed;

        var targetWidth = isSidebarCollapsed ? 60.0 : 220.0;
        var currentWidth = SidebarColumn.Width.Value;
        var duration = TimeSpan.FromMilliseconds(200);
        var easing = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };

        // Animate toggle icon rotation
        var rotationAnimation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = isSidebarCollapsed ? 0 : 180,
            To = isSidebarCollapsed ? 180 : 0,
            Duration = duration,
            EasingFunction = easing
        };
        ToggleIconRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rotationAnimation);

        // Animate the column width using a timer-based approach
        AnimateSidebarWidth(currentWidth, targetWidth, duration);

        // Toggle visibility of text elements
        var textVisibility = isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

        // Hide/show text labels
        SetSidebarTextVisibility(textVisibility);

        // Update section headers
        SectionMain.Visibility = textVisibility;
        SectionResources.Visibility = textVisibility;
        SectionSystem.Visibility = textVisibility;

        // Update header
        SidebarHeader.Visibility = textVisibility;
        VersionText.Visibility = textVisibility;
        BottomSeparator.Visibility = textVisibility;

        // Hide submenu when collapsed
        if (isSidebarCollapsed)
        {
            configSubmenu.Visibility = Visibility.Collapsed;
            ConfigsChevron.Visibility = Visibility.Collapsed;
        }
        else
        {
            ConfigsChevron.Visibility = Visibility.Visible;
        }
    }

    private void AnimateSidebarWidth(double from, double to, TimeSpan duration)
    {
        var startTime = DateTime.Now;
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60fps
        };

        timer.Tick += (s, e) =>
        {
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            var progress = Math.Min(elapsed / duration.TotalMilliseconds, 1.0);

            // Apply easing (quadratic ease-in-out)
            var easedProgress = progress < 0.5
                ? 2 * progress * progress
                : 1 - Math.Pow(-2 * progress + 2, 2) / 2;

            var currentWidth = from + (to - from) * easedProgress;
            SidebarColumn.Width = new GridLength(currentWidth);

            if (progress >= 1.0)
            {
                timer.Stop();
                SidebarColumn.Width = new GridLength(to);
            }
        };

        timer.Start();
    }

    private void SetSidebarTextVisibility(Visibility visibility)
    {
        // Main navigation items
        menuOptionHomeText.Visibility = visibility;
        menuOptionJobsText.Visibility = visibility;
        menuOptionHitsText.Visibility = visibility;
        menuOptionConfigsText.Visibility = visibility;
        menuOptionWordlistsText.Visibility = visibility;
        menuOptionProxiesText.Visibility = visibility;
        menuOptionToolsText.Visibility = visibility;
        menuOptionPluginsText.Visibility = visibility;
        menuOptionSettingsText.Visibility = visibility;
        menuOptionRLSettingsText.Visibility = visibility;
        menuOptionCheckUpdateText.Visibility = visibility;
        menuOptionAboutText.Visibility = visibility;
    }

    private void InitializeSidebarState()
    {
        // Set initial state for collapsed sidebar
        if (isSidebarCollapsed)
        {
            var textVisibility = Visibility.Collapsed;

            // Hide text labels
            SetSidebarTextVisibility(textVisibility);

            // Update section headers
            SectionMain.Visibility = textVisibility;
            SectionResources.Visibility = textVisibility;
            SectionSystem.Visibility = textVisibility;

            // Update header
            SidebarHeader.Visibility = textVisibility;
            VersionText.Visibility = textVisibility;
            BottomSeparator.Visibility = textVisibility;

            // Hide submenu elements
            configSubmenu.Visibility = Visibility.Collapsed;
            ConfigsChevron.Visibility = Visibility.Collapsed;

            // Set toggle icon rotation to indicate collapsed state
            ToggleIconRotation.Angle = 180;
        }
    }
    #endregion Sidebar Toggle Logic

    #region Dropdown submenu logic
    private void ConfigSubmenuMouseEnter(object sender, MouseEventArgs e)
    {
        if (vm.IsConfigSelected)
        {
            hoveringConfigSubmenu = true;
            configSubmenu.Visibility = Visibility.Visible;
            // UpdateConfigSubmenuPosition(); // Removed
        }
    }

    private async void ConfigSubmenuMouseLeave(object sender, MouseEventArgs e)
    {
        hoveringConfigSubmenu = false;
        await CheckCloseSubmenuAsync();
    }

    private void ConfigsMenuOptionMouseEnter(object sender, MouseEventArgs e)
    {
        if (vm.IsConfigSelected)
        {
            hoveringConfigsMenuOption = true;
            configSubmenu.Visibility = Visibility.Visible;
            // UpdateConfigSubmenuPosition(); // Removed
        }
    }

    private async void ConfigsMenuOptionMouseLeave(object sender, MouseEventArgs e)
    {
        hoveringConfigsMenuOption = false;
        await CheckCloseSubmenuAsync();
    }

    private async Task CheckCloseSubmenuAsync()
    {
        await Task.Delay(200); // Increased delay for better user experience

        if (!hoveringConfigSubmenu && !hoveringConfigsMenuOption)
        {
            configSubmenu.Visibility = Visibility.Collapsed;
        }
    }

    private void CloseSubmenu() => configSubmenu.Visibility = Visibility.Collapsed;
    #endregion Dropdown submenu logic

    private void ApplyAccessibilitySettings()
    {
        var accessibility = AccessibilitySettings;
        if (openBulletSettingsService.Settings.AccessibilitySettings == null)
        {
            openBulletSettingsService.Settings.AccessibilitySettings = accessibility;
        }

        themeService.ApplyAccessibilitySettings();

        // Control-specific accessibility
        if (accessibility.EnableHighContrast)
        {
            // Already handled by themeService.ApplyAccessibilitySettings() calling ApplyHighContrastPalette internally?
            // Wait, ThemeService.ApplyAccessibilitySettings calls ApplyHighContrastPalette.
            // So we don't need to call it here.
        }

        var focusStyle = accessibility.AlwaysShowFocusVisuals
            ? TryFindResource("HighVisibilityFocusStyle") as Style
            : null;

        foreach (var button in navigationButtons.Where(static b => b != null))
        {
            button.FocusVisualStyle = focusStyle;
            ApplyButtonSpacing(button, accessibility.UseComfortableSpacing);
            ConfigureTooltips(button, accessibility.ShowHelpfulTooltips);
        }

        var submenuButtons = new[]
        {
            menuOptionMetadata,
            menuOptionReadme,
            menuOptionStacker,
            menuOptionLoliCode,
            menuOptionConfigSettings,
            menuOptionCSharpCode
        };

        foreach (var button in submenuButtons.Where(static b => b != null))
        {
            button.FocusVisualStyle = focusStyle;
            ApplyButtonSpacing(button, accessibility.UseComfortableSpacing);
            ConfigureTooltips(button, accessibility.ShowHelpfulTooltips);
        }

        if (configSubmenu != null)
        {
            ConfigureTooltips(configSubmenu, accessibility.ShowHelpfulTooltips);
        }
    }



    private static void ApplyButtonSpacing(Button button, bool comfortable)
    {
        if (button == null)
        {
            return;
        }

        button.Padding = comfortable ? new Thickness(14, 10, 14, 10) : new Thickness(8, 6, 8, 6);
        button.Margin = comfortable ? new Thickness(4, 0, 4, 0) : new Thickness(2, 0, 2, 0);
    }

    private static void ConfigureTooltips(DependencyObject target, bool helpful)
    {
        if (target == null)
        {
            return;
        }

        if (helpful)
        {
            ToolTipService.SetInitialShowDelay(target, 150);
            ToolTipService.SetShowDuration(target, 12000);
            ToolTipService.SetBetweenShowDelay(target, 300);
        }
        else
        {
            ToolTipService.SetInitialShowDelay(target, 400);
            ToolTipService.SetShowDuration(target, 4000);
        }
    }

}

