using MahApps.Metro.Controls;
using OpenBullet2.Core.Models.Settings;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.Views.Pages;
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

    private readonly Button[] labels;
    private readonly Button[] navigationButtons; // Modern navigation buttons array
    
    private readonly HotkeyService hotkeyService;
    private readonly OpenBulletSettingsService openBulletSettingsService;
    private readonly ConfigService configService;
    private readonly IConfigRepository configRepository;
    private readonly IAppUpdateService appUpdateService;

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

    // Centralized navigation mapping to eliminate duplication
    private static readonly Dictionary<string, MainWindowPage> MenuNavigationMap = new()
    {
        ["menuOptionHome"] = MainWindowPage.Home,
        ["menuOptionJobs"] = MainWindowPage.Jobs,
        ["menuOptionMonitor"] = MainWindowPage.Monitor,
        ["menuOptionProxies"] = MainWindowPage.Proxies,
        ["menuOptionWordlists"] = MainWindowPage.Wordlists,
        ["menuOptionConfigs"] = MainWindowPage.Configs,
        ["menuOptionHits"] = MainWindowPage.Hits,
        ["menuOptionPlugins"] = MainWindowPage.Plugins,
        ["menuOptionSettings"] = MainWindowPage.OBSettings,
        ["menuOptionRLSettings"] = MainWindowPage.RLSettings,
        ["menuOptionCheckUpdate"] = MainWindowPage.CheckUpdate,
        ["menuOptionAbout"] = MainWindowPage.About,
        ["menuOptionMetadata"] = MainWindowPage.ConfigMetadata,
        ["menuOptionReadme"] = MainWindowPage.ConfigReadme,
        ["menuOptionStacker"] = MainWindowPage.ConfigStacker,
        ["menuOptionLoliCode"] = MainWindowPage.ConfigLoliCode,
        ["menuOptionConfigSettings"] = MainWindowPage.ConfigSettings,
        ["menuOptionCSharpCode"] = MainWindowPage.ConfigCSharpCode
    };

    // Public property to access the config editor page
    public ConfigEditor ConfigEditorPage => configEditorPage;

    /// <summary>
    /// Responsive design properties
    /// </summary>

    public MainWindow(
        MainWindowViewModel viewModel,
        HotkeyService hotkeyService,
        OpenBulletSettingsService openBulletSettingsService,
        ConfigService configService,
        IConfigRepository configRepository,
        IAppUpdateService appUpdateService)
    {
        vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
        this.openBulletSettingsService = openBulletSettingsService ?? throw new ArgumentNullException(nameof(openBulletSettingsService));
        this.configService = configService ?? throw new ArgumentNullException(nameof(configService));
        this.configRepository = configRepository ?? throw new ArgumentNullException(nameof(configRepository));
        this.appUpdateService = appUpdateService ?? throw new ArgumentNullException(nameof(appUpdateService));

        DataContext = vm;
        Closing += vm.OnWindowClosing;

        InitializeComponent();

        Loaded += OnWindowLoaded;
        SizeChanged += OnWindowSizeChanged;
        StateChanged += OnWindowStateChanged;
        LocationChanged += OnWindowLocationChanged;

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
            menuOptionCheckUpdate,
            menuOptionConfigs,
            menuOptionConfigSettings,
            menuOptionCSharpCode,
            menuOptionHits,
            menuOptionHome,
            menuOptionJobs,
            menuOptionLoliCode,
            menuOptionMetadata,
            menuOptionMonitor,
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
            menuOptionMonitor,
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
        SetTheme(customization);
    }

    #region Responsive Design Methods

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
            var customizationSettings = openBulletSettingsService.Settings.CustomizationSettings;

        if (customizationSettings.RememberWindowState)
        {
            // Load saved window state
            Width = Math.Max(MinWidth, customizationSettings.WindowWidth);
            Height = Math.Max(MinHeight, customizationSettings.WindowHeight);

            var workingArea = SystemParameters.WorkArea;

            // Ensure window position is within screen bounds
            Left = Math.Max(0, Math.Min(customizationSettings.WindowLeft, workingArea.Right - Width));
            Top = Math.Max(0, Math.Min(customizationSettings.WindowTop, workingArea.Bottom - Height));

            // Restore window state
            WindowState = (WindowState)customizationSettings.WindowState;
        }
        else
        {
            // Use default sizing logic for new installations
            var workingArea = SystemParameters.WorkArea;
            var dpiScale = Media.VisualTreeHelper.GetDpi(this);

            // Calculate responsive base size based on screen dimensions and DPI
            var screenWidth = workingArea.Width;
            var screenHeight = workingArea.Height;
            var dpiFactor = dpiScale.DpiScaleX;

            // Responsive sizing: use 70% of screen size on larger screens, 85% on smaller screens
            var targetScreenPercentage = (screenWidth <= 1366 || screenHeight <= 768) ? 0.85 : 0.70;
            
            var baseWidth = Math.Min(1200, screenWidth * targetScreenPercentage / dpiFactor);
            var baseHeight = Math.Min(800, screenHeight * targetScreenPercentage / dpiFactor);

            // Set window size with better constraints
            var maxWidth = workingArea.Width * 0.95;
            var maxHeight = workingArea.Height * 0.95;

            Width = Math.Max(MinWidth, Math.Min(baseWidth, maxWidth));
            Height = Math.Max(MinHeight, Math.Min(baseHeight, maxHeight));

            // Center window with better positioning
            Left = Math.Max(0, (workingArea.Width - Width) / 2 + workingArea.Left);
            Top = Math.Max(0, (workingArea.Height - Height) / 2 + workingArea.Top);

            // Ensure window is fully visible on screen
            if (Left + Width > workingArea.Right)
                Left = workingArea.Right - Width;
            if (Top + Height > workingArea.Bottom)
                Top = workingArea.Bottom - Height;
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateConfigSubmenuPosition();
        UpdateResponsiveLayout();
        SaveWindowState();
    }
    
    /// <summary>
    /// Updates responsive layout elements based on current window size
    /// </summary>
    private void UpdateResponsiveLayout()
    {
        try
        {
            // Force update of data triggers by refreshing the binding context
            // This ensures responsive styles are re-evaluated when window size changes
            var currentWidth = ActualWidth;
            
            // Trigger layout update for responsive elements
            if (Root != null)
            {
                Root.UpdateLayout();
            }
            
            // Update navigation menu responsiveness
            UpdateNavigationResponsiveness(currentWidth);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating responsive layout: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Updates navigation menu responsiveness based on window width
    /// </summary>
    private void UpdateNavigationResponsiveness(double windowWidth)
    {
        try
        {
            // Additional responsive logic can be added here if needed
            // For now, the XAML data triggers handle most of the responsive behavior
            
            // Force refresh of config submenu position if it's visible
            if (configSubmenu?.Visibility == Visibility.Visible)
            {
                UpdateConfigSubmenuPosition();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating navigation responsiveness: {ex.Message}");
        }
    }

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
        // Suspend/resume debugger updates during minimize/restore to improve performance
        SaveWindowState();
        NotifyDebuggerWindowStateChanged(WindowState == WindowState.Minimized);
    }

    private void NotifyDebuggerWindowStateChanged(bool isMinimized)
    {
        try
        {
            // Check if we're on a config editor page that contains the debugger
            if (configEditorPage?.debuggerFrame?.Content is Views.Pages.Shared.Debugger debugger)
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

    private void OnWindowLocationChanged(object sender, EventArgs e)
    {
        SaveWindowState();
    }

    private void SaveWindowState()
    {
        try
        {
            var customizationSettings = openBulletSettingsService.Settings.CustomizationSettings;

            if (customizationSettings.RememberWindowState && WindowState != WindowState.Minimized)
            {
                // Only save size and position when not minimized
                if (WindowState == WindowState.Normal)
                {
                    customizationSettings.WindowWidth = Width;
                    customizationSettings.WindowHeight = Height;
                    customizationSettings.WindowLeft = Left;
                    customizationSettings.WindowTop = Top;
                }

                customizationSettings.WindowState = (int)WindowState;

                // Save settings to disk
                _ = Task.Run(() => openBulletSettingsService.SaveAsync());
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash the application
            System.Diagnostics.Debug.WriteLine($"Error saving window state: {ex.Message}");
        }
    }



    private void UpdateConfigSubmenuPosition()
    {
        if (FindName("menuOptionConfigs") is FrameworkElement configsButton && configSubmenu != null)
        {
            var position = configsButton.TransformToAncestor(this).Transform(new Point(0, 0));
            configSubmenu.Margin = new Thickness(position.X, position.Y + configsButton.ActualHeight + 5, 0, 0);
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

        // Handle page navigation
        HandleOtherPageNavigation(page);

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

    private void HandleOtherPageNavigation(MainWindowPage page)
    {
        // Consolidated page navigation with consistent patterns
        switch (page)
        {
            case MainWindowPage.Home:
                CreateAndNavigateToPage(() => new Home(), ref homePage, menuOptionHome);
                break;
            case MainWindowPage.Monitor:
                CreateAndNavigateToPage(() => new Monitor(), ref monitorPage, menuOptionMonitor);
                break;
            case MainWindowPage.Proxies:
                CreateAndNavigateToPage(() => new Proxies(), ref proxiesPage, menuOptionProxies, updateViewModel: true);
                break;
            case MainWindowPage.Wordlists:
                CreateAndNavigateToPage(() => new Wordlists(), ref wordlistsPage, menuOptionWordlists);
                break;
            case MainWindowPage.Configs:
                CreateAndNavigateToPage(() => new Configs(), ref configsPage, menuOptionConfigs, updateViewModel: true);
                break;
            case MainWindowPage.Hits:
                CreateAndNavigateToPage(() => new Hits(), ref hitsPage, menuOptionHits, updateViewModel: true);
                break;
            case MainWindowPage.Plugins:
                CreateAndNavigateToPage(() => new Plugins(), ref pluginsPage, menuOptionPlugins);
                break;
            case MainWindowPage.OBSettings:
                CreateAndNavigateToPage(() => new OBSettings(), ref obSettingsPage, menuOptionSettings);
                break;
            case MainWindowPage.RLSettings:
                CreateAndNavigateToPage(() => new RLSettings(), ref rlSettingsPage, menuOptionRLSettings);
                break;


            // Config-related pages use separate methods for complex logic
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
        }
    }

    // Helper method to consolidate repetitive page creation patterns
    private void CreateAndNavigateToPage<T>(Func<T> pageFactory, ref T pageField, Button menuButton, bool updateViewModel = false)
        where T : Page
    {
        var pageTypeName = typeof(T).Name;
        try
        {
            System.Diagnostics.Debug.WriteLine($"Starting navigation to {pageTypeName}");

            // Enhanced logging for page creation
            if (pageField == null)
            {
                System.Diagnostics.Debug.WriteLine($"Creating new instance of {pageTypeName}");
                try
                {
                    pageField = pageFactory();
                    System.Diagnostics.Debug.WriteLine($"Successfully created {pageTypeName}");
                }
                catch (Exception createEx)
                {
                    // Enhanced error logging for page creation failures
                    var errorDetails = $"Failed to create {pageTypeName}: {createEx.GetType().Name} - {createEx.Message}";
                    if (createEx.InnerException != null)
                    {
                        errorDetails += $" | Inner: {createEx.InnerException.GetType().Name} - {createEx.InnerException.Message}";
                    }

                    System.Diagnostics.Debug.WriteLine(errorDetails);

                    // Log to crash system for better debugging
                    try
                    {
                        var geh = Resources["GlobalExceptionHandler"] as Infrastructure.Diagnostics.GlobalExceptionHandler;
                        if (geh != null)
                        {
                            Infrastructure.Diagnostics.CrashLoggingService.Instance.LogCrash(
                                createEx,
                                $"MainWindow.CreateAndNavigateToPage<{pageTypeName}>",
                                $"Page creation failed during navigation to {pageTypeName}. Menu: {menuButton?.Name ?? "Unknown"}",
                                false);
                        }
                    }
                    catch { /* Ignore logging errors */ }

                    throw; // Re-throw to be caught by outer try-catch
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Reusing existing instance of {pageTypeName}");
            }

            // Call UpdateViewModel if the page supports it and updateViewModel is true
            if (updateViewModel)
            {
                System.Diagnostics.Debug.WriteLine($"Updating ViewModel for {pageTypeName}");
                try
                {
                    var updateMethod = pageField?.GetType().GetMethod("UpdateViewModel", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (updateMethod != null)
                    {
                        updateMethod.Invoke(pageField, null);
                        System.Diagnostics.Debug.WriteLine($"Successfully updated ViewModel for {pageTypeName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"No UpdateViewModel method found for {pageTypeName}");
                    }
                }
                catch (Exception updateEx)
                {
                    var errorDetails = $"Failed to update ViewModel for {pageTypeName}: {updateEx.GetType().Name} - {updateEx.Message}";
                    if (updateEx.InnerException != null)
                    {
                        errorDetails += $" | Inner: {updateEx.InnerException.GetType().Name} - {updateEx.InnerException.Message}";
                    }

                    System.Diagnostics.Debug.WriteLine(errorDetails);

                    // Log ViewModel update failures
                    try
                    {
                        Infrastructure.Diagnostics.CrashLoggingService.Instance.LogCrash(
                            updateEx,
                            $"MainWindow.UpdateViewModel<{pageTypeName}>",
                            $"ViewModel update failed for {pageTypeName}. Menu: {menuButton?.Name ?? "Unknown"}",
                            false);
                    }
                    catch { /* Ignore logging errors */ }

                    throw; // Re-throw to be caught by outer try-catch
                }
            }

            System.Diagnostics.Debug.WriteLine($"Calling ChangePage for {pageTypeName}");
            ChangePage(pageField, menuButton);
            System.Diagnostics.Debug.WriteLine($"Successfully navigated to {pageTypeName}");
        }
        catch (Exception ex)
        {
            var errorDetails = $"Navigation to {pageTypeName} failed: {ex.GetType().Name} - {ex.Message}";
            if (ex.InnerException != null)
            {
                errorDetails += $" | Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}";
            }

            System.Diagnostics.Debug.WriteLine(errorDetails);
            System.Diagnostics.Debug.WriteLine($"Full stack trace: {ex}");

            // Enhanced crash logging with full context
            try
            {
                Infrastructure.Diagnostics.CrashLoggingService.Instance.LogCrash(
                        ex,
                        $"MainWindow.CreateAndNavigateToPage<{pageTypeName}>",
                        $"Complete navigation failure for {pageTypeName}. Menu: {menuButton?.Name ?? "Unknown"}, UpdateViewModel: {updateViewModel}",
                        false);
            }
            catch { /* Ignore logging errors */ }

            // Show enhanced error message to user
            var userMessage = $"Failed to open {pageTypeName} page.\n\nError: {ex.Message}";
            if (ex.InnerException != null)
            {
                userMessage += $"\n\nInner Error: {ex.InnerException.Message}";
            }
            userMessage += $"\n\nCheck the crash logs in UserData/Logs/Crashes for detailed information.";

            Alert.Error("Navigation Error", userMessage);
        }
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


    private void HandleConfigEditorNavigation(ConfigEditorSection section, Button menuButton)
    {
        if (vm.Config != null && (vm.Config.Mode is ConfigMode.Stack or ConfigMode.LoliCode || (section == ConfigEditorSection.CSharp && vm.Config.Mode == ConfigMode.CSharp)))
        {
            if (configEditorPage == null)
            {
                configEditorPage = new ConfigEditor();
            }
            configEditorPage.NavigateTo(section);
            ChangePage(configEditorPage, menuButton);

            // Update UI to ensure buttons are visible
            configEditorPage.UpdateUI();
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
            NavigateTo(targetPage);
        }
        else
        {
            // Fallback to name-based mapping for compatibility
            var page = MenuNavigationMap.TryGetValue(button?.Name ?? "", out var fallbackPage)
                ? fallbackPage
                : MainWindowPage.Home;
            NavigateTo(page);
        }
    }

    private void ChangePage(Page newPage, Button newButton)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"ChangePage: Setting page to {newPage?.GetType().Name ?? "null"}");
            CurrentPage = newPage;
            MainFrame.Content = newPage;

            // Optimized button selection - only update if different from current
            if (newButton != currentSelectedButton)
            {
                if (currentSelectedButton != null)
                {
                    // Clear previous selection visual state
                    currentSelectedButton.Tag = null;
                }

                if (newButton != null)
                {
                    // Mark as selected. XAML style triggers will update appearance accordingly
                    newButton.Tag = "Selected";
                    currentSelectedButton = newButton;
                }
            }
            vm.IsLoading = false;
            System.Diagnostics.Debug.WriteLine($"ChangePage: Successfully changed to {newPage?.GetType().Name ?? "null"}");
        }
        catch (Exception ex)
        {
            var errorDetails = $"ChangePage failed for {newPage?.GetType().Name ?? "unknown"}: {ex.GetType().Name} - {ex.Message}";
            if (ex.InnerException != null)
            {
                errorDetails += $" | Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}";
            }

            System.Diagnostics.Debug.WriteLine(errorDetails);
            System.Diagnostics.Debug.WriteLine($"Full ChangePage error: {ex}");

            // Log navigation failures
            try
            {
                Infrastructure.Diagnostics.CrashLoggingService.Instance.LogCrash(
                    ex,
                    "MainWindow.ChangePage",
                    $"Failed to change page to {newPage?.GetType().Name ?? "unknown"}. Button: {newButton?.Name ?? "Unknown"}",
                    false);
            }
            catch { /* Ignore logging errors */ }

            vm.IsLoading = false;
            throw; // Re-throw so calling code can handle it
        }
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

    // Consolidated navigation handlers - removed 10 redundant methods
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

    Hits = 13,
    Plugins = 14,
    OBSettings = 15,
    RLSettings = 16,
    CheckUpdate = 17,
    About = 18
}
