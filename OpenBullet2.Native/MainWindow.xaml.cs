using MahApps.Metro.Controls;
using OpenBullet2.Core.Models.Settings;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.Utils;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.Views.Pages;
using RuriLib.Models.Configs;
using RuriLib.Models.Jobs;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenBullet2.Native;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : MetroWindow
{
    private readonly MainWindowViewModel vm;

    private bool hoveringConfigsMenuOption;
    private bool hoveringConfigSubmenu;

    private readonly TextBlock[] labels;

    private Home homePage;
    private Jobs jobsPage;
    private Monitor monitorPage;
    private MultiRunJobViewer multiRunJobViewerPage;
    private ProxyCheckJobViewer proxyCheckJobViewerPage;
    private Proxies proxiesPage;
    private Wordlists wordlistsPage;
    private Configs configsPage;
    private Views.Pages.ConfigMetadata configMetadataPage;
    private ConfigReadme configReadmePage;
    private ConfigEditor configEditorPage;
    private Views.Pages.ConfigSettings configSettingsPage;
    private Hits hitsPage;
    private OBSettings obSettingsPage;
    private RLSettings rlSettingsPage;
    private Plugins pluginsPage;
    private About aboutPage;

    public Page CurrentPage { get; private set; }

    /// <summary>
    /// Responsive design properties
    /// </summary>
    private bool isInitialLoad = true;
    private WindowState previousWindowState;
    private bool firstRestoreFromMaximized = true;

    public MainWindow()
    {
        vm = new MainWindowViewModel();
        DataContext = vm;
        Closing += vm.OnWindowClosing;

        InitializeComponent();

        // Command Bindings for Configs
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.NewConfig, OnNewConfigExecuted, OnCanExecuteConfigCommand));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.OpenConfig, OnOpenConfigExecuted, OnCanExecuteConfigCommand));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.SaveConfig, OnSaveConfigExecuted, OnCanExecuteConfigCommand));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.Refresh, OnRefreshExecuted, OnCanExecuteRefreshCommand));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.Quit, OnQuitExecuted));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToHome, OnNavigateToHomeExecuted));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToJobs, OnNavigateToJobsExecuted));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToMonitor, OnNavigateToMonitorExecuted));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToProxies, OnNavigateToProxiesExecuted));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToWordlists, OnNavigateToWordlistsExecuted));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToConfigs, OnNavigateToConfigsExecuted));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToHits, OnNavigateToHitsExecuted));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToPlugins, OnNavigateToPluginsExecuted));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToOBSettings, OnNavigateToOBSettingsExecuted));
        _ = CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToRLSettings, OnNavigateToRLSettingsExecuted));

        labels =
        [
            menuOptionAbout,
            menuOptionConfigs,
            menuOptionConfigSettings,
            menuOptionCSharpCode,
            menuOptionHits,
            menuOptionHome,
            menuOptionJobs,
            menuOptionLoliCode,
            menuOptionLoliScript,
            menuOptionMetadata,
            menuOptionMonitor,
            menuOptionSettings,
            menuOptionPlugins,
            menuOptionProxies,
            menuOptionReadme,
            menuOptionRLSettings,
            menuOptionStacker,
            menuOptionWordlists
        ];

        // Pages to initialize as soon as the program starts. This is done to reduce the loading time
        // when clicking on them, as it can be frustrating for the user on specific pages.
        configsPage = new();

        Title = "OpenBullet 2 - 0.3.3 [akunlama MOD]";

        // Initialize HotkeyService
        var hotkeyService = SP.GetService<HotkeyService>();
        hotkeyService.Initialize(this);

        // Set the theme
        var obSettingsService = SP.GetService<OpenBulletSettingsService>();
        var customization = obSettingsService.Settings.CustomizationSettings;
        SetTheme(customization);

        // Store initial window state
        previousWindowState = WindowState;
    }

    #region Responsive Design Methods

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (isInitialLoad)
        {
            SetOptimalWindowSize();
            AdjustLayoutForResolution();
            isInitialLoad = false;
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        AdjustLayoutForResolution();
        UpdateConfigSubmenuPosition();
    }

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
        HandleWindowStateChange();
        previousWindowState = WindowState;
    }

    private void SetOptimalWindowSize()
    {
        try
        {
            // Skip setting size if window is maximized - let our restore logic handle minimal sizing
            if (WindowState == WindowState.Maximized)
            {
                System.Diagnostics.Debug.WriteLine("Window is maximized - skipping optimal size setting to allow restore logic");
                return;
            }

            // Get screen dimensions
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var workAreaWidth = SystemParameters.WorkArea.Width;
            var workAreaHeight = SystemParameters.WorkArea.Height;

            // Calculate optimal size based on screen resolution
            double optimalWidth, optimalHeight;

            if (screenWidth >= 2560) // 4K and above
            {
                optimalWidth = Math.Min(1800, workAreaWidth * 0.75);
                optimalHeight = Math.Min(1200, workAreaHeight * 0.8);
            }
            else if (screenWidth >= 1920) // Full HD
            {
                optimalWidth = Math.Min(1600, workAreaWidth * 0.8);
                optimalHeight = Math.Min(1000, workAreaHeight * 0.85);
            }
            else if (screenWidth >= 1600) // HD+
            {
                optimalWidth = Math.Min(1400, workAreaWidth * 0.85);
                optimalHeight = Math.Min(900, workAreaHeight * 0.9);
            }
            else if (screenWidth >= 1366) // HD
            {
                optimalWidth = Math.Min(1200, workAreaWidth * 0.9);
                optimalHeight = Math.Min(800, workAreaHeight * 0.9);
            }
            else // Smaller resolutions
            {
                optimalWidth = Math.Min(1024, workAreaWidth * 0.95);
                optimalHeight = Math.Min(600, workAreaHeight * 0.95);
            }

            // Ensure minimum size requirements
            optimalWidth = Math.Max(optimalWidth, MinWidth);
            optimalHeight = Math.Max(optimalHeight, MinHeight);

            // Set the calculated size
            Width = optimalWidth;
            Height = optimalHeight;

            // Center the window
            Left = (screenWidth - Width) / 2;
            Top = (screenHeight - Height) / 2;

            System.Diagnostics.Debug.WriteLine($"Optimal size set: {Width}x{Height} on {screenWidth}x{screenHeight} screen");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting optimal size: {ex.Message}");
            // Fallback to default size only if not maximized
            if (WindowState != WindowState.Maximized)
            {
                Width = 1400;
                Height = 900;
            }
        }
    }

    private void AdjustLayoutForResolution()
    {
        try
        {
            var currentWidth = ActualWidth;
            var currentHeight = ActualHeight;

            // Adjust margins and padding based on window size
            if (WindowState == WindowState.Maximized)
            {
                // Fullscreen - no margins
                LeftMarginColumn.Width = new GridLength(0);
                RightMarginColumn.Width = new GridLength(0);
                BottomMarginRow.Height = new GridLength(0);
                TopNavRow.Height = new GridLength(60);

                NavigationHeader.Margin = new Thickness(0);
                MainContentBorder.Margin = new Thickness(0);
                NavigationGrid.Margin = new Thickness(16, 8, 16, 8);
                mainFrame.Margin = new Thickness(16);
            }
            else
            {
                // Windowed mode - adaptive margins
                var marginSize = Math.Max(8, Math.Min(24, currentWidth * 0.015));

                LeftMarginColumn.Width = new GridLength(marginSize);
                RightMarginColumn.Width = new GridLength(marginSize);
                BottomMarginRow.Height = new GridLength(marginSize);
                TopNavRow.Height = new GridLength(60);

                NavigationHeader.Margin = new Thickness(0, marginSize * 0.5, 0, marginSize * 0.5);
                MainContentBorder.Margin = new Thickness(0, marginSize * 0.5, 0, 0);
                NavigationGrid.Margin = new Thickness(16, 8, 16, 8);
                mainFrame.Margin = new Thickness(16);
            }

            // Adjust navigation menu for smaller screens
            AdjustNavigationForSize(currentWidth);

            System.Diagnostics.Debug.WriteLine($"Layout adjusted for {currentWidth}x{currentHeight}, State: {WindowState}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adjusting layout: {ex.Message}");
        }
    }

    private void AdjustNavigationForSize(double width)
    {
        // Navigation is now simple and responsive via CSS-like styling in XAML
        // No complex responsive logic needed - the working version handles this automatically
    }

    private void HandleWindowStateChange()
    {
        try
        {
            if (WindowState == WindowState.Maximized)
            {
                // Handle maximize
                System.Diagnostics.Debug.WriteLine("Window maximized - adjusting for fullscreen");
                AdjustLayoutForResolution();
            }
            else if (previousWindowState == WindowState.Maximized && WindowState == WindowState.Normal)
            {
                // Handle restore from maximize
                System.Diagnostics.Debug.WriteLine("Window restored from maximize");
                AdjustLayoutForResolution();
            }
            else if (WindowState == WindowState.Minimized)
            {
                // Handle minimize
                System.Diagnostics.Debug.WriteLine("Window minimized");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error handling window state change: {ex.Message}");
        }
    }

    private void UpdateConfigSubmenuPosition()
    {
        try
        {
            if (configSubmenu.Visibility == Visibility.Visible)
            {
                // Calculate position relative to configs menu option
                var configsPosition = menuOptionConfigs.TransformToAncestor(Root).Transform(new Point(0, 0));
                var margin = WindowState == WindowState.Maximized ? 0 : LeftMarginColumn.Width.Value;

                configSubmenu.Margin = new Thickness(
                    configsPosition.X + margin,
                    configsPosition.Y + menuOptionConfigs.ActualHeight + 8,
                    0, 0);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating submenu position: {ex.Message}");
        }
    }

    #endregion Responsive Design Methods

    public async Task NavigateTo(MainWindowPage page)
    {
        vm.IsLoading = true;

        // Needed to save the content of the LoliCode editor when changing page
        if (CurrentPage == configEditorPage)
        {
            configEditorPage?.OnPageChanged();
        }

        // Handle Jobs page navigation directly to avoid threading issues
        if (page == MainWindowPage.Jobs)
        {
            await HandleJobsPageNavigation();
            vm.IsLoading = false;
            return;
        }

        // Simulate async loading to show the indicator for other pages
        await HandleOtherPageNavigation(page);

        vm.IsLoading = false;
    }

    private async Task HandleJobsPageNavigation()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("Direct Jobs navigation");
            jobsPage ??= new();
            System.Diagnostics.Debug.WriteLine("Jobs page created successfully");
            ChangePage(jobsPage, menuOptionJobs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Direct Jobs navigation error: {ex.Message}");
            Alert.Exception(ex);
        }
    }

    private async Task HandleOtherPageNavigation(MainWindowPage page)
    {
        await Task.Run(() =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                switch (page)
                {
                    case MainWindowPage.Home:
                        NavigateToHomePage();
                        break;
                    case MainWindowPage.Monitor:
                        NavigateToMonitorPage();
                        break;
                    case MainWindowPage.Proxies:
                        NavigateToProxiesPage();
                        break;
                    case MainWindowPage.Wordlists:
                        NavigateToWordlistsPage();
                        break;
                    case MainWindowPage.Configs:
                        NavigateToConfigsPage();
                        break;
                    case MainWindowPage.Hits:
                        NavigateToHitsPage();
                        break;
                    case MainWindowPage.Plugins:
                        NavigateToPluginsPage();
                        break;
                    case MainWindowPage.OBSettings:
                        NavigateToOBSettingsPage();
                        break;
                    case MainWindowPage.RLSettings:
                        NavigateToRLSettingsPage();
                        break;
                    case MainWindowPage.About:
                        NavigateToAboutPage();
                        break;
                    case MainWindowPage.ConfigMetadata:
                        NavigateToConfigMetadataPage();
                        break;
                    case MainWindowPage.ConfigReadme:
                        NavigateToConfigReadmePage();
                        break;
                    case MainWindowPage.ConfigStacker:
                        NavigateToConfigStackerPage();
                        break;
                    case MainWindowPage.ConfigLoliCode:
                        NavigateToConfigLoliCodePage();
                        break;
                    case MainWindowPage.ConfigSettings:
                        NavigateToConfigSettingsPage();
                        break;
                    case MainWindowPage.ConfigCSharpCode:
                        NavigateToConfigCSharpCodePage();
                        break;
                    case MainWindowPage.ConfigLoliScript:
                        NavigateToConfigLoliScriptPage();
                        break;
                    default:
                        break;
                }
            });
        });
    }

    private void NavigateToHomePage()
    {
        homePage = new Home();
        ChangePage(homePage, menuOptionHome);
    }
    private void NavigateToMonitorPage()
    {
        if (monitorPage == null) monitorPage = new();
        ChangePage(monitorPage, menuOptionMonitor);
    }
    private void NavigateToProxiesPage()
    {
        if (proxiesPage == null) proxiesPage = new Proxies();
        proxiesPage.UpdateViewModel();
        ChangePage(proxiesPage, menuOptionProxies);
    }
    private void NavigateToWordlistsPage()
    {
        if (wordlistsPage == null) wordlistsPage = new Wordlists();
        ChangePage(wordlistsPage, menuOptionWordlists);
    }
    private void NavigateToConfigsPage()
    {
        if (configsPage == null) configsPage = new Configs();
        configsPage.UpdateViewModel();
        ChangePage(configsPage, menuOptionConfigs);
    }
    private void NavigateToHitsPage()
    {
        if (hitsPage == null) hitsPage = new Hits();
        hitsPage.UpdateViewModel();
        ChangePage(hitsPage, menuOptionHits);
    }
    private void NavigateToPluginsPage()
    {
        if (pluginsPage == null) pluginsPage = new Plugins();
        ChangePage(pluginsPage, menuOptionPlugins);
    }
    private void NavigateToOBSettingsPage()
    {
        if (obSettingsPage == null) obSettingsPage = new OBSettings();
        ChangePage(obSettingsPage, menuOptionSettings);
    }
    private void NavigateToRLSettingsPage()
    {
        if (rlSettingsPage == null) rlSettingsPage = new RLSettings();
        ChangePage(rlSettingsPage, menuOptionRLSettings);
    }
    private void NavigateToAboutPage()
    {
        if (aboutPage == null) aboutPage = new About();
        ChangePage(aboutPage, menuOptionAbout);
    }
    private void NavigateToConfigMetadataPage()
    {
        CloseSubmenu();
        if (configMetadataPage == null) configMetadataPage = new Views.Pages.ConfigMetadata();
        configMetadataPage.UpdateViewModel();
        ChangePage(configMetadataPage, menuOptionMetadata);
    }
    private void NavigateToConfigReadmePage()
    {
        CloseSubmenu();
        if (configReadmePage == null) configReadmePage = new ConfigReadme();
        configReadmePage.UpdateViewModel();
        ChangePage(configReadmePage, menuOptionReadme);
    }
    private void NavigateToConfigStackerPage()
    {
        CloseSubmenu();
        HandleConfigEditorNavigation(ConfigEditorSection.Stacker, menuOptionStacker);
    }
    private void NavigateToConfigLoliCodePage()
    {
        CloseSubmenu();
        HandleConfigEditorNavigation(ConfigEditorSection.LoliCode, menuOptionLoliCode);
    }
    private void NavigateToConfigSettingsPage()
    {
        CloseSubmenu();
        if (configSettingsPage == null) configSettingsPage = new Views.Pages.ConfigSettings();
        configSettingsPage.UpdateViewModel();
        ChangePage(configSettingsPage, menuOptionConfigSettings);
    }
    private void NavigateToConfigCSharpCodePage()
    {
        CloseSubmenu();
        HandleConfigEditorNavigation(ConfigEditorSection.CSharp, menuOptionCSharpCode);
    }
    private void NavigateToConfigLoliScriptPage()
    {
        CloseSubmenu();
        HandleConfigEditorNavigation(ConfigEditorSection.LoliScript, menuOptionLoliScript);
    }

    private void HandleConfigEditorNavigation(ConfigEditorSection section, TextBlock menuOption)
    {
        if (vm.Config != null && (vm.Config.Mode is ConfigMode.Stack or ConfigMode.LoliCode || (section == ConfigEditorSection.CSharp && vm.Config.Mode == ConfigMode.CSharp) || (section == ConfigEditorSection.LoliScript && vm.Config.Mode == ConfigMode.Legacy)))
        {
            if (configEditorPage == null)
            {
                configEditorPage = new ConfigEditor();
            }
            configEditorPage.NavigateTo(section);
            ChangePage(configEditorPage, menuOption);
        }
    }

    public void DisplayJob(JobViewModel jobVM)
    {
        switch (jobVM)
        {
            case MultiRunJobViewModel mrj:
                if (multiRunJobViewerPage == null)
                {
                    multiRunJobViewerPage = new MultiRunJobViewer();
                }
                multiRunJobViewerPage.BindViewModel(mrj);
                ChangePage(multiRunJobViewerPage, null);
                break;

            case ProxyCheckJobViewModel pcj:
                if (proxyCheckJobViewerPage == null)
                {
                    proxyCheckJobViewerPage = new ProxyCheckJobViewer();
                }
                proxyCheckJobViewerPage.BindViewModel(pcj);
                ChangePage(proxyCheckJobViewerPage, null);
                break;
            default:
                break;
        }
    }

    public void EditJob(JobViewModel jobVM)
    {
        NavigateTo(MainWindowPage.Jobs);
        jobsPage.EditJob(jobVM);
    }

    private void OpenHomePage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.Home);
    private void OpenJobsPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.Jobs);
    private void OpenMonitorPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.Monitor);
    private void OpenProxiesPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.Proxies);
    private void OpenWordlistsPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.Wordlists);
    private void OpenConfigsPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.Configs);
    private void OpenHitsPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.Hits);
    private void OpenPluginsPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.Plugins);
    private void OpenOBSettingsPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.OBSettings);
    private void OpenRLSettingsPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.RLSettings);
    private void OpenAboutPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.About);

    private void OpenMetadataPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.ConfigMetadata);
    private void OpenReadmePage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.ConfigReadme);
    private void OpenStackerPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.ConfigStacker);
    private void OpenLoliCodePage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.ConfigLoliCode);
    private void OpenConfigSettingsPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.ConfigSettings);
    private void OpenCSharpCodePage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.ConfigCSharpCode);
    private void OpenLoliScriptPage(object sender, MouseEventArgs e) => NavigateTo(MainWindowPage.ConfigLoliScript);

    private void ChangePage(Page newPage, TextBlock newLabel)
    {
        CurrentPage = newPage;
        mainFrame.Content = newPage;

        // Update the selected menu item
        foreach (var label in labels)
        {
            label.Foreground = Brush.Get("ForegroundMain");
        }

        if (newLabel is not null)
        {
            newLabel.Foreground = Brush.Get("ForegroundMenuSelected");
        }
        vm.IsLoading = false;
    }

    private void OnCanExecuteConfigCommand(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = CurrentPage == configsPage ||
                      CurrentPage == configEditorPage ||
                      CurrentPage == configMetadataPage ||
                      CurrentPage == configReadmePage ||
                      CurrentPage == configSettingsPage;

    private void OnNewConfigExecuted(object sender, ExecutedRoutedEventArgs e) => configsPage.Create(null, null);

    private void OnOpenConfigExecuted(object sender, ExecutedRoutedEventArgs e) => configsPage.Edit(null, null);

    private void OnSaveConfigExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (CurrentPage == configsPage)
        {
            configsPage.Save(null, null);
        }
        else if (CurrentPage == configEditorPage)
        {
            configEditorPage.Save(null, null);
        }
        // For other config pages like metadata, readme, settings, we can also trigger save via the configEditorPage
        else if (CurrentPage == configMetadataPage ||
                 CurrentPage == configReadmePage ||
                 CurrentPage == configSettingsPage)
        {
            // Create a temporary configEditor if needed and save
            if (configEditorPage != null)
            {
                configEditorPage.Save(null, null);
            }
            else
            {
                // Fallback to using ConfigService directly
                var configService = SP.GetService<ConfigService>();
                var configRepo = SP.GetService<IConfigRepository>();
                if (configService.SelectedConfig != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await configRepo.SaveAsync(configService.SelectedConfig);
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
        }
    }

    private async void OnRefreshExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (CurrentPage == configsPage)
        {
            configsPage.Rescan(null, null);
        }
        else if (CurrentPage == hitsPage)
        {
            await hitsPage.Refresh();
        }
        else if (CurrentPage == proxiesPage)
        {
            await proxiesPage.Refresh();
        }
        else if (CurrentPage == wordlistsPage)
        {
            await wordlistsPage.Refresh();
        }
        else if (CurrentPage == pluginsPage)
        {
            pluginsPage.Refresh();
        }
    }

    private void OnCanExecuteRefreshCommand(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = CurrentPage == configsPage ||
                       CurrentPage == hitsPage ||
                       CurrentPage == proxiesPage ||
                       CurrentPage == wordlistsPage ||
                       CurrentPage == pluginsPage;

    private void OnQuitExecuted(object sender, ExecutedRoutedEventArgs e) => Application.Current.Shutdown();

    private void OnNavigateToHomeExecuted(object sender, ExecutedRoutedEventArgs e) => NavigateTo(MainWindowPage.Home);

    private void OnNavigateToJobsExecuted(object sender, ExecutedRoutedEventArgs e) => NavigateTo(MainWindowPage.Jobs);

    private void OnNavigateToMonitorExecuted(object sender, ExecutedRoutedEventArgs e) => NavigateTo(MainWindowPage.Monitor);

    private void OnNavigateToProxiesExecuted(object sender, ExecutedRoutedEventArgs e) => NavigateTo(MainWindowPage.Proxies);

    private void OnNavigateToWordlistsExecuted(object sender, ExecutedRoutedEventArgs e) => NavigateTo(MainWindowPage.Wordlists);

    private void OnNavigateToConfigsExecuted(object sender, ExecutedRoutedEventArgs e) => NavigateTo(MainWindowPage.Configs);

    private void OnNavigateToHitsExecuted(object sender, ExecutedRoutedEventArgs e) => NavigateTo(MainWindowPage.Hits);

    private void OnNavigateToPluginsExecuted(object sender, ExecutedRoutedEventArgs e) => NavigateTo(MainWindowPage.Plugins);

    private void OnNavigateToOBSettingsExecuted(object sender, ExecutedRoutedEventArgs e) => NavigateTo(MainWindowPage.OBSettings);

    private void OnNavigateToRLSettingsExecuted(object sender, ExecutedRoutedEventArgs e) => NavigateTo(MainWindowPage.RLSettings);

    private void TakeScreenshot(object sender, RoutedEventArgs e)
    {
        try
        {
            // Add visual feedback with smaller, more subtle indication
            var originalContent = screenshotButton.Content;
            screenshotButton.Content = new TextBlock
            {
                Text = "📸 Saved",
                FontSize = 8,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            screenshotButton.IsEnabled = false;

            // Take window-only screenshot (captures only OpenBullet2 window content)
            Screenshot.Take(this);

            // Reset button after a short delay
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    screenshotButton.Content = originalContent;
                    screenshotButton.IsEnabled = true;
                });
            });
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
            // Reset button immediately on error
            screenshotButton.Content = "Screenshot";
            screenshotButton.IsEnabled = true;
        }
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestoreWindow(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;

            // Set minimal size on first restore from maximized
            if (firstRestoreFromMaximized)
            {
                // Set to absolute minimum size for maximum compactness
                Width = MinWidth;  // 1024 from XAML
                Height = MinHeight; // 600 from XAML

                // Center the window
                var screenWidth = SystemParameters.PrimaryScreenWidth;
                var screenHeight = SystemParameters.PrimaryScreenHeight;
                Left = (screenWidth - Width) / 2;
                Top = (screenHeight - Height) / 2;

                firstRestoreFromMaximized = false;
            }
        }
        else
        {
            WindowState = WindowState.Maximized;
            // Reset the flag when returning to maximized state
            firstRestoreFromMaximized = true;
        }
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    #region Dropdown submenu logic
    private void ConfigSubmenuMouseEnter(object sender, MouseEventArgs e)
    {
        if (vm.IsConfigSelected)
        {
            hoveringConfigSubmenu = true;
            configSubmenu.Visibility = Visibility.Visible;
            UpdateConfigSubmenuPosition();
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
            UpdateConfigSubmenuPosition();
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

    public void SetTheme(CustomizationSettings customization)
    {
        Brush.SetAppColor("BackgroundMain", customization.BackgroundMain);
        Brush.SetAppColor("BackgroundSecondary", customization.BackgroundSecondary);
        Brush.SetAppColor("BackgroundInput", customization.BackgroundInput);
        Brush.SetAppColor("ForegroundMain", customization.ForegroundMain);
        Brush.SetAppColor("ForegroundInput", customization.ForegroundInput);
        Brush.SetAppColor("ForegroundGood", customization.ForegroundGood);
        Brush.SetAppColor("ForegroundBad", customization.ForegroundBad);
        Brush.SetAppColor("ForegroundCustom", customization.ForegroundCustom);
        Brush.SetAppColor("ForegroundRetry", customization.ForegroundRetry);
        Brush.SetAppColor("ForegroundBanned", customization.ForegroundBanned);
        Brush.SetAppColor("ForegroundToCheck", customization.ForegroundToCheck);
        Brush.SetAppColor("ForegroundMenuSelected", customization.ForegroundMenuSelected);
        Brush.SetAppColor("SuccessButton", customization.SuccessButton);
        Brush.SetAppColor("PrimaryButton", customization.PrimaryButton);
        Brush.SetAppColor("WarningButton", customization.WarningButton);
        Brush.SetAppColor("DangerButton", customization.DangerButton);
        Brush.SetAppColor("ForegroundButton", customization.ForegroundButton);
        Brush.SetAppColor("BackgroundButton", customization.BackgroundButton);

        // BACKGROUND
        Background = File.Exists(customization.BackgroundImagePath)
            ? new System.Windows.Media.ImageBrush(
                new System.Windows.Media.Imaging.BitmapImage(
                    new Uri(customization.BackgroundImagePath)))
            {
                Opacity = customization.BackgroundOpacity / 100,
                Stretch = System.Windows.Media.Stretch.UniformToFill
            }
            : Brush.Get("BackgroundMain");
    }
}

public class MainWindowViewModel : ViewModelBase
{
    private readonly JobManagerService jobManagerService;
    private readonly ConfigService configService;
    public event Action<Config> ConfigSelected;
    public Config Config => configService.SelectedConfig;

    private bool isLoading;
    public bool IsLoading
    {
        get => isLoading;
        set
        {
            isLoading = value;
            OnPropertyChanged();
        }
    }

    public bool IsConfigSelected => Config != null;

    public MainWindowViewModel()
    {
        jobManagerService = SP.GetService<JobManagerService>();
        configService = SP.GetService<ConfigService>();
        configService.OnConfigSelected += (_, config) =>
        {
            OnPropertyChanged(nameof(IsConfigSelected));
            ConfigSelected?.Invoke(config);
        };
    }

    public void OnWindowClosing(object sender, CancelEventArgs e)
    {
        var obSettingsService = SP.GetService<OpenBulletSettingsService>();

        // Check if the config was saved
        if (obSettingsService.Settings.GeneralSettings.WarnConfigNotSaved && Config?.HasUnsavedChanges() == true)
        {
            e.Cancel = !Alert.Confirm("Config not saved", $"The config you are editing ({Config.Metadata.Name}) has unsaved changes, are you sure you want to quit?", nameof(obSettingsService.Settings.GeneralSettings.WarnConfigNotSaved));
        }

        // Check if there are jobs running
        if (!e.Cancel && jobManagerService.Jobs.Any(static j => j.Status != JobStatus.Idle))
        {
            e.Cancel = !Alert.Confirm("Job(s) running", "One or more jobs are still running, are you sure you want to quit?", "PerformConfirmationOnDestructiveActions");
        }
    }
}

public enum MainWindowPage
{
    Home = 0,
    Jobs = 1,
    Monitor = 2,
    Proxies = 3,
    Wordlists = 4,
    Configs = 5,
    ConfigMetadata = 6,
    ConfigReadme = 7,
    ConfigStacker = 8,
    ConfigLoliCode = 9,
    ConfigSettings = 10,
    ConfigCSharpCode = 11,
    ConfigLoliScript = 12,
    Hits = 13,
    Plugins = 14,
    OBSettings = 15,
    RLSettings = 16,
    About = 17
}
