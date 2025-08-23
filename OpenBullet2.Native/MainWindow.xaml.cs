using MahApps.Metro.Controls;
using OpenBullet2.Core.Models.Settings;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Infrastructure.DependencyInjection;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.Views.Pages;
using RuriLib.Models.Configs;
using RuriLib.Models.Jobs;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.IO.Compression;
using System.Windows.Input;
using Newtonsoft.Json;
using System.Media;
using System.Windows.Threading;

namespace OpenBullet2.Native;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : MetroWindow
{
    private readonly MainWindowViewModel vm;

    private bool hoveringConfigsMenuOption;
    private bool hoveringConfigSubmenu;
    private TextBlock currentSelectedLabel; // Track current selected label for optimization

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

    // Public property to access the config editor page
    public ConfigEditor ConfigEditorPage => configEditorPage;

    /// <summary>
    /// Responsive design properties
    /// </summary>

    public MainWindow()
    {
        vm = new MainWindowViewModel();
        DataContext = vm;
        Closing += vm.OnWindowClosing;

        InitializeComponent();

        Loaded += OnWindowLoaded;
        SizeChanged += OnWindowSizeChanged;

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
            menuOptionMetadata,
            menuOptionMonitor,
            menuOptionSettings,
            menuOptionPlugins,
            menuOptionProxies,
            menuOptionReadme,
            menuOptionRLSettings,
            menuOptionStacker,
            menuOptionUpdate,
            menuOptionWordlists
        ];

        // Lazy initialization - pages created only when needed
        // This reduces initial memory usage and improves startup time

        Title = "OpenBullet 2 - 0.3.3.6 [akunlama MOD]";

        // Initialize HotkeyService
        var hotkeyService = ServiceLocator.GetService<HotkeyService>();
        hotkeyService.Initialize(this);

        // Set the theme
        var obSettingsService = ServiceLocator.GetService<OpenBulletSettingsService>();
        var customization = obSettingsService.Settings.CustomizationSettings;
        SetTheme(customization);
    }

    #region Responsive Design Methods

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        var workingArea = SystemParameters.WorkArea;
        Width = Math.Min(1400, workingArea.Width * 0.9);
        Height = Math.Min(900, workingArea.Height * 0.9);
        Left = (workingArea.Width - Width) / 2;
        Top = (workingArea.Height - Height) / 2;
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateConfigSubmenuPosition();
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
        // Optimized page creation with immediate navigation
        switch (page)
        {
            case MainWindowPage.Home:
                try
                {
                    homePage ??= new Home();
                    ChangePage(homePage, menuOptionHome);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Home page creation error: {ex.Message}");
                    Alert.Exception(ex);
                }
                break;
            case MainWindowPage.Monitor:
                monitorPage ??= new();
                ChangePage(monitorPage, menuOptionMonitor);
                break;
            case MainWindowPage.Proxies:
                proxiesPage ??= new Proxies();
                proxiesPage?.UpdateViewModel();
                ChangePage(proxiesPage, menuOptionProxies);
                break;
            case MainWindowPage.Wordlists:
                wordlistsPage ??= new Wordlists();
                ChangePage(wordlistsPage, menuOptionWordlists);
                break;
            case MainWindowPage.Configs:
                configsPage ??= new Configs();
                configsPage?.UpdateViewModel();
                ChangePage(configsPage, menuOptionConfigs);
                break;
            case MainWindowPage.Hits:
                hitsPage ??= new Hits();
                hitsPage?.UpdateViewModel();
                ChangePage(hitsPage, menuOptionHits);
                break;
            case MainWindowPage.Plugins:
                pluginsPage ??= new Plugins();
                ChangePage(pluginsPage, menuOptionPlugins);
                break;
            case MainWindowPage.OBSettings:
                obSettingsPage ??= new OBSettings();
                ChangePage(obSettingsPage, menuOptionSettings);
                break;
            case MainWindowPage.RLSettings:
                rlSettingsPage ??= new RLSettings();
                ChangePage(rlSettingsPage, menuOptionRLSettings);
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

            default:
                break;
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


    private void HandleConfigEditorNavigation(ConfigEditorSection section, TextBlock menuOption)
    {
        if (vm.Config != null && (vm.Config.Mode is ConfigMode.Stack or ConfigMode.LoliCode || (section == ConfigEditorSection.CSharp && vm.Config.Mode == ConfigMode.CSharp)))
        {
            if (configEditorPage == null)
            {
                configEditorPage = new ConfigEditor();
            }
            configEditorPage.NavigateTo(section);
            ChangePage(configEditorPage, menuOption);

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

    // Consolidated navigation handler - reduced from 18 individual methods
    private void HandleNavigation(object sender, MouseEventArgs e)
    {
        var element = sender as FrameworkElement;

        // Handle update check separately
        if (element?.Name == "menuOptionUpdate")
        {
            CheckForUpdates();
            return;
        }

        var page = element?.Name switch
        {
            "menuOptionHome" => MainWindowPage.Home,
            "menuOptionJobs" => MainWindowPage.Jobs,
            "menuOptionMonitor" => MainWindowPage.Monitor,
            "menuOptionProxies" => MainWindowPage.Proxies,
            "menuOptionWordlists" => MainWindowPage.Wordlists,
            "menuOptionConfigs" => MainWindowPage.Configs,
            "menuOptionHits" => MainWindowPage.Hits,
            "menuOptionPlugins" => MainWindowPage.Plugins,
            "menuOptionSettings" => MainWindowPage.OBSettings,
            "menuOptionRLSettings" => MainWindowPage.RLSettings,
            "menuOptionAbout" => MainWindowPage.About,
            "menuOptionMetadata" => MainWindowPage.ConfigMetadata,
            "menuOptionReadme" => MainWindowPage.ConfigReadme,
            "menuOptionStacker" => MainWindowPage.ConfigStacker,
            "menuOptionLoliCode" => MainWindowPage.ConfigLoliCode,
            "menuOptionConfigSettings" => MainWindowPage.ConfigSettings,
            "menuOptionCSharpCode" => MainWindowPage.ConfigCSharpCode,

            _ => MainWindowPage.Home
        };
        NavigateTo(page);
    }

    private void ChangePage(Page newPage, TextBlock newLabel)
    {
        CurrentPage = newPage;
        mainFrame.Content = newPage;

        // Optimized label selection - only update if different from current
        if (newLabel != currentSelectedLabel)
        {
            if (currentSelectedLabel != null)
            {
                // Clear previous selection visual state
                currentSelectedLabel.Tag = null;
            }

            if (newLabel != null)
            {
                // Mark as selected. XAML style triggers will update Foreground accordingly
                newLabel.Tag = "Selected";
                currentSelectedLabel = newLabel;
            }
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
                var configService = ServiceLocator.GetService<ConfigService>();
                var configRepo = ServiceLocator.GetService<IConfigRepository>();
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

    private void CheckForUpdates()
    {
        // Ensure we run the whole update flow on the UI thread (STA)
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(CheckForUpdates);
            return;
        }

        // Fire-and-forget wrapper that keeps UI thread as the owner of created windows
        _ = RunUpdateFlowOnStaAsync();
    }

    private async Task RunUpdateFlowOnStaAsync()
    {
        try
        {
            // Clean up old update files in background (fire-and-forget with guard)
            _ = Task.Run(async () =>
            {
                try { await CleanupOldUpdateFiles().ConfigureAwait(false); }
                catch (Exception bgEx) { Debug.WriteLine($"Cleanup task error: {bgEx.Message}"); }
            });

            // Show checking notification (non-blocking) on UI thread
            Alert.Success("Update Check", "Checking for updates...");

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenBullet2-Native-Updater/1.0");

            const string latestUrl = "https://api.github.com/repos/ocim128/OB2-Edited/releases/latest";

            async Task<HttpResponseMessage> GetWithRetryAsync(string url, int attempts = 3)
            {
                var delay = 1000;
                for (var i = 1; i <= attempts; i++)
                {
                    try
                    {
                        var resp = await httpClient.GetAsync(url).ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode) return resp;
                        if ((int)resp.StatusCode is >= 500 and < 600)
                        {
                            await Task.Delay(delay).ConfigureAwait(false);
                            delay *= 2;
                            continue;
                        }
                        return resp;
                    }
                    catch (TaskCanceledException) when (i < attempts)
                    {
                        await Task.Delay(delay).ConfigureAwait(false);
                        delay *= 2;
                    }
                    catch (HttpRequestException) when (i < attempts)
                    {
                        await Task.Delay(delay).ConfigureAwait(false);
                        delay *= 2;
                    }
                }
                return await httpClient.GetAsync(url).ConfigureAwait(false);
            }

            var response = await GetWithRetryAsync(latestUrl).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // UI must own MessageBox
                Dispatcher.Invoke(() =>
                    MessageBox.Show(
                        "Failed to check for updates. Please check your internet connection or visit:\nhttps://github.com/ocim128/OB2-Edited/releases",
                        "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            var jsonContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var releaseInfo = JsonConvert.DeserializeObject<dynamic>(jsonContent);

            var latestVersion = (string)releaseInfo.tag_name;
            var currentVersionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
            string currentVersion = "Unknown";

            if (File.Exists(currentVersionPath))
            {
                try
                {
                    currentVersion = (await File.ReadAllTextAsync(currentVersionPath).ConfigureAwait(false)).Trim();
                }
                catch (Exception ioEx)
                {
                    Debug.WriteLine($"Failed reading version.txt: {ioEx.Message}");
                }
            }

            if (!string.Equals(currentVersion, "Unknown", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(currentVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.Invoke(() =>
                {
                    Alert.Success("Update Check", "You are already running the latest version!");
                    PlayPopSound();
                });
                return;
            }

            await DownloadAndInstallUpdate(releaseInfo, latestVersion).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Ensure UI ownership for alerts
            Dispatcher.Invoke(() =>
                Alert.Error("Update Error",
                    $"Failed to check for updates: {ex.Message}\n\nPlease visit manually:\nhttps://github.com/ocim128/OB2-Edited/releases"));
        }
    }

    private async Task DownloadAndInstallUpdate(dynamic releaseInfo, string latestVersion)
    {
        try
        {
            // Find the appropriate asset to download
            var assets = releaseInfo.assets;
            string downloadUrl = null;
            long fileSize = 0;

            foreach (var asset in assets)
            {
                string assetName = asset.name.ToString().ToLower();
                if (assetName.Contains("windows") || assetName.Contains("win") || assetName.EndsWith(".zip") || assetName.EndsWith(".rar") || assetName.Contains("ob2"))
                {
                    downloadUrl = asset.browser_download_url.ToString();
                    fileSize = asset.size != null ? (long)asset.size : 0;
                    break;
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("No suitable download found for Windows. Opening release page...",
                        "Download Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                });

                var startInfo = new ProcessStartInfo
                {
                    FileName = releaseInfo.html_url.ToString(),
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                return;
            }

            // Create temp directory for download with timestamp
            var tempDir = Path.Combine(Path.GetTempPath(), $"OpenBullet2Update_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(tempDir);

            // Get current directory and create backup
            var currentDir = AppDomain.CurrentDomain.BaseDirectory;
            var backupDir = Path.Combine(tempDir, "backup");
            Directory.CreateDirectory(backupDir);

            // Create backup of current installation
            await CreateBackup(currentDir, backupDir);

            // Determine file extension from download URL
            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            var downloadPath = Path.Combine(tempDir, fileName);

            // Check if file already exists and is valid
            bool needsDownload = true;
            if (File.Exists(downloadPath))
            {
                try
                {
                    var existingFileInfo = new FileInfo(downloadPath);

                    // Use asset size if available, otherwise check with server
                    long expectedSize = fileSize;
                    if (expectedSize == 0)
                    {
                        using var httpClient = new HttpClient();
                        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenBullet2-Native-Updater/1.0");
                        var headResponse = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, downloadUrl));
                        expectedSize = headResponse.Content.Headers.ContentLength ?? 0;
                    }

                    // Verify file integrity with checksum if possible
                    if (expectedSize > 0 && existingFileInfo.Length == expectedSize && await VerifyFileIntegrity(downloadPath))
                    {
                        needsDownload = false;
                        MessageBox.Show("Update file already downloaded and verified. Proceeding with installation...",
                            "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        // Remove partial/corrupted download
                        File.Delete(downloadPath);
                    }
                }
                catch
                {
                    // If we can't verify, delete and re-download
                    try { File.Delete(downloadPath); } catch { }
                }
            }

            // Show progress dialog
            // Create UI elements on the UI thread to keep STA ownership
            Window progressWindow = null!;
            ProgressBar progressBar = null!;
            Label statusLabel = null!;

            await Dispatcher.InvokeAsync(() =>
            {
                progressWindow = new Window
                {
                    Title = "Downloading Update",
                    Width = 400,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    Topmost = true
                };

                progressBar = new ProgressBar
                {
                    Margin = new Thickness(20),
                    Height = 20
                };

                statusLabel = new Label
                {
                    Content = "Downloading...",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(20, 10, 20, 0)
                };

                var stackPanel = new StackPanel();
                stackPanel.Children.Add(statusLabel);
                stackPanel.Children.Add(progressBar);
                progressWindow.Content = stackPanel;

                progressWindow.Show();
            });


            // Download the file only if needed
            if (needsDownload)
            {
                int maxRetries = 3;
                int retryDelay = 2000; // 2 seconds

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        statusLabel.Dispatcher.Invoke(() => statusLabel.Content = attempt > 1 ? $"Downloading (Attempt {attempt}/{maxRetries})..." : "Downloading...");

                        using var httpClient = new HttpClient
                        {
                            Timeout = TimeSpan.FromMinutes(10) // tighter timeout
                        };
                        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenBullet2-Native-Updater/1.0");

                        var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? 0;
                        var downloadedBytes = 0L;
                        var startTime = DateTime.Now;

                        using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                        var buffer = new byte[8192];
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                            downloadedBytes += bytesRead;

                            if (totalBytes > 0)
                            {
                                var progress = (double)downloadedBytes / totalBytes * 100;
                                var elapsed = DateTime.Now - startTime;
                                var speed = downloadedBytes / Math.Max(1, elapsed.TotalSeconds);
                                var eta = speed > 0 ? TimeSpan.FromSeconds((totalBytes - downloadedBytes) / speed) : TimeSpan.Zero;

                                progressBar.Dispatcher.Invoke(() => progressBar.Value = progress);
                                statusLabel.Dispatcher.Invoke(() =>
                                    statusLabel.Content =
                                        $"Downloaded {downloadedBytes / 1024d / 1024d:F1} MB of {totalBytes / 1024d / 1024d:F1} MB ({progress:F1}%)\n" +
                                        $"Speed: {speed / 1024d / 1024d:F1} MB/s, ETA: {eta:mm\\:ss}");
                            }
                            else
                            {
                                statusLabel.Dispatcher.Invoke(() => statusLabel.Content = $"Downloaded: {downloadedBytes / 1024d / 1024d:F1} MB");
                            }
                        }

                        // Verify download completed successfully
                        if (totalBytes > 0 && downloadedBytes != totalBytes)
                        {
                            throw new Exception($"Download incomplete: {downloadedBytes}/{totalBytes} bytes");
                        }

                        break; // Success, exit retry loop
                    }
                    catch (Exception ex) when (attempt < maxRetries)
                    {
                        statusLabel.Dispatcher.Invoke(() => statusLabel.Content = $"Download failed (Attempt {attempt}/{maxRetries}): {ex.Message}\nRetrying in {retryDelay / 1000} seconds...");
                        await Task.Delay(retryDelay);
                        retryDelay *= 2; // Exponential backoff

                        // Clean up partial download
                        try { File.Delete(downloadPath); } catch { }
                    }
                    catch (Exception ex) when (attempt < maxRetries)
                    {
                        statusLabel.Dispatcher.Invoke(() => statusLabel.Content = $"Download failed (Attempt {attempt}/{maxRetries}): {ex.Message}\nRetrying in {retryDelay / 1000} seconds...");
                        await Task.Delay(retryDelay).ConfigureAwait(false);
                        retryDelay *= 2; // Exponential backoff

                        // Clean up partial download
                        try { File.Delete(downloadPath); } catch { }
                    }
                    catch (Exception ex) when (attempt == maxRetries)
                    {
                        throw new Exception($"Download failed after {maxRetries} attempts: {ex.Message}", ex);
                    }
                }
            }
            else
            {
                // File already exists, skip download
                progressBar.Dispatcher.Invoke(() => progressBar.Value = 100);
                statusLabel.Dispatcher.Invoke(() => statusLabel.Content = "Using existing verified download...");
            }

            await Dispatcher.InvokeAsync(() =>
            {
                statusLabel.Content = "Extracting...";
                progressBar.Value = 100;
            });

            // Current directory already defined above for backup

            // Extract the file
            var extractPath = Path.Combine(tempDir, "extracted");

            // Clean up any existing extraction directory
            if (Directory.Exists(extractPath))
            {
                try
                {
                    Directory.Delete(extractPath, true);
                }
                catch
                {
                    // If we can't delete, create a new unique directory
                    extractPath = Path.Combine(tempDir, $"extracted_{DateTime.Now.Ticks}");
                }
            }

            Directory.CreateDirectory(extractPath);

            var fileExtension = Path.GetExtension(downloadPath).ToLower();
            if (fileExtension == ".zip")
            {
                try
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(downloadPath, extractPath);

                    // Validate extraction
                    if (!Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories).Any())
                    {
                        throw new InvalidOperationException("Extraction resulted in no files");
                    }

                    // Check if extraction created a single subfolder (common in GitHub releases)
                    var extractedItems = Directory.GetDirectories(extractPath);
                    if (extractedItems.Length == 1 && !Directory.GetFiles(extractPath).Any())
                    {
                        // If there's only one subfolder and no files in root, use the subfolder as source
                        extractPath = extractedItems[0];
                    }
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() => progressWindow.Close());
                    await Dispatcher.InvokeAsync(() =>
                        MessageBox.Show($"Failed to extract update file: {ex.Message}\n\nPlease download and extract manually.",
                            "Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error));
                    return;
                }
            }
            else if (fileExtension == ".rar")
            {
                // For .rar files, we need to handle them differently since .NET doesn't support RAR natively
                await Dispatcher.InvokeAsync(() => progressWindow.Close());
                MessageBox.Show(
                    $"Downloaded update file: {fileName}\n\n" +
                    $"This is a RAR archive. Please extract it manually to:\n{currentDir}\n\n" +
                    $"The file has been saved to: {downloadPath}\n\n" +
                    $"After extraction, restart the application.",
                    "Manual Extraction Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Open the temp directory for user
                Process.Start("explorer.exe", tempDir);
                return;
            }
            else
            {
                await Dispatcher.InvokeAsync(() => progressWindow.Close());
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Unsupported file format: {fileExtension}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                return;
            }

            await Dispatcher.InvokeAsync(() => progressWindow.Close());

            // Automatically proceed with installation - no confirmation needed
            {
                // Create update script
                var updateScript = Path.Combine(tempDir, "update.bat");
                var rollbackScript = Path.Combine(tempDir, "rollback.bat");
                var exePath = Process.GetCurrentProcess().MainModule.FileName;

                var versionFile = Path.Combine(currentDir, "version.txt");
                var logFile = Path.Combine(tempDir, "update.log");

                // Create streamlined update script without user interaction
                var scriptContent = "@echo off\n" +
                    "setlocal enabledelayedexpansion\n" +
                    $"set LOGFILE={logFile}\n" +
                    "echo %date% %time% - Starting OpenBullet2 Update Installation >> %LOGFILE%\n" +
                    "timeout /t 3 /nobreak > nul\n" +
                    "taskkill /f /im OpenBullet2.Native.exe 2>nul\n" +
                    "timeout /t 2 /nobreak > nul\n" +
                    "echo Creating rollback script...\n" +
                    $"echo @echo off > \"{rollbackScript}\"\n" +
                    $"echo xcopy /E /Y /R \"{backupDir}\\*\" \"{currentDir}\" 2^>nul >> \"{rollbackScript}\"\n" +
                    $"echo start \"\" \"{exePath}\" >> \"{rollbackScript}\"\n" +
                    "set RETRY_COUNT=0\n" +
                    ":retry_copy\n" +
                    "set /a RETRY_COUNT+=1\n" +
                    "echo %date% %time% - Copying files (attempt %RETRY_COUNT%) >> %LOGFILE%\n" +
                    $"xcopy /E /Y /R \"{extractPath}\\*\" \"{currentDir}\" 2>>%LOGFILE%\n" +
                    "if errorlevel 1 (\n" +
                    "    if %RETRY_COUNT% LSS 5 (\n" +
                    "        timeout /t 2 /nobreak > nul\n" +
                    "        goto retry_copy\n" +
                    "    ) else (\n" +
                    "        echo %date% %time% - CRITICAL: File copy failed after 5 attempts >> %LOGFILE%\n" +
                    $"        call \"{rollbackScript}\"\n" +
                    "        exit /b 1\n" +
                    "    )\n" +
                    ")\n" +
                    "set VERSION_RETRY=0\n" +
                    ":retry_version\n" +
                    "set /a VERSION_RETRY+=1\n" +
                    $"echo {latestVersion} > \"{versionFile}\" 2>>%LOGFILE%\n" +
                    "if errorlevel 1 (\n" +
                    "    if %VERSION_RETRY% LSS 3 (\n" +
                    "        timeout /t 1 /nobreak > nul\n" +
                    "        goto retry_version\n" +
                    "    )\n" +
                    ")\n" +
                    $"if not exist \"{Path.Combine(currentDir, "OpenBullet2.Native.exe")}\" (\n" +
                    "    echo %date% %time% - CRITICAL: Main executable missing, running rollback >> %LOGFILE%\n" +
                    $"    call \"{rollbackScript}\"\n" +
                    "    exit /b 1\n" +
                    ")\n" +
                    "echo %date% %time% - Update completed successfully >> %LOGFILE%\n" +
                    $"start \"\" \"{exePath}\"\n" +
                    "timeout /t 5 /nobreak > nul\n" +
                    $"rd /s /q \"{tempDir}\" 2>nul\n" +
                    $"del \"{updateScript}\" 2>nul\n" +
                    "exit";

                await File.WriteAllTextAsync(updateScript, scriptContent);

                // Start the update process
                var updateProcess = new ProcessStartInfo
                {
                    FileName = updateScript,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(updateProcess);

                // Close the current application
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            // Log the full exception to file
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update_error.log");
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Update Error: {ex}\n\n";
                File.AppendAllText(logPath, logEntry);
            }
            catch { /* ignore */ }

            // Only show a MessageBox for non-thread-ownership errors
            var msg = ex.Message ?? string.Empty;
            var isCrossThreadUiError =
                msg.Contains("different thread owns it", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("The calling thread cannot access this object", StringComparison.OrdinalIgnoreCase);

            if (!isCrossThreadUiError)
            {
                var errorMessage = $"Update failed: {ex.Message}\n\n";

                var backupDirs = Directory.GetDirectories(Path.GetTempPath(), "OpenBullet2Update_*")
                    .Where(d => Directory.Exists(Path.Combine(d, "backup")))
                    .OrderByDescending(d => Directory.GetCreationTime(d))
                    .ToArray();

                if (backupDirs.Any())
                {
                    var latestBackup = Path.Combine(backupDirs.First(), "backup");
                    errorMessage += $"A backup is available at: {latestBackup}\n";
                    errorMessage += "You can manually restore from this backup if needed.\n\n";
                }

                errorMessage += "Please download manually from:\nhttps://github.com/ocim128/OB2-Edited/releases";

                Dispatcher.Invoke(() =>
                    MessageBox.Show(errorMessage, "Update Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }
    }

    private async Task CreateBackup(string sourceDir, string backupDir)
    {
        try
        {
            var importantFiles = new[] { "*.exe", "*.dll", "*.config", "*.json", "version.txt" };

            foreach (var pattern in importantFiles)
            {
                var files = Directory.GetFiles(sourceDir, pattern, SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var backupPath = Path.Combine(backupDir, fileName);
                    File.Copy(file, backupPath, true);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Backup creation failed: {ex.Message}");
        }
    }

    private async Task CleanupOldUpdateFiles()
    {
        try
        {
            var tempPath = Path.GetTempPath();
            var cutoffDate = DateTime.Now.AddDays(-7); // Keep files for 7 days

            // Clean up old update directories
            var oldUpdateDirs = Directory.GetDirectories(tempPath, "OpenBullet2Update_*")
                .Where(d => Directory.GetCreationTime(d) < cutoffDate)
                .ToArray();

            foreach (var dir in oldUpdateDirs)
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            // Clean up old log files (keep last 10)
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update_error.log");
            if (File.Exists(logPath))
            {
                var logInfo = new FileInfo(logPath);
                if (logInfo.Length > 1024 * 1024) // If log is larger than 1MB
                {
                    var lines = await File.ReadAllLinesAsync(logPath);
                    if (lines.Length > 100)
                    {
                        // Keep only the last 50 entries
                        var recentLines = lines.TakeLast(50).ToArray();
                        await File.WriteAllLinesAsync(logPath, recentLines);
                    }
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private async Task<bool> VerifyFileIntegrity(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            var fileInfo = new FileInfo(filePath);

            // Check if file is empty
            if (fileInfo.Length == 0)
                return false;

            // Basic integrity check - ensure file can be opened and is not corrupted
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[Math.Min(8192, (int)Math.Min(fileInfo.Length, int.MaxValue))];
            var bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

            if (bytesRead == 0)
                return false;

            var extension = Path.GetExtension(filePath).ToLower();

            // For ZIP files, try to open as archive and validate structure
            if (extension == ".zip")
            {
                try
                {
                    using var archive = System.IO.Compression.ZipFile.OpenRead(filePath);
                    if (archive.Entries.Count == 0)
                        return false;

                    // Try to read the first entry to ensure archive is not corrupted
                    var firstEntry = archive.Entries.First();
                    using var entryStream = firstEntry.Open();
                    var testBuffer = new byte[Math.Min(1024, (int)Math.Max(1, firstEntry.Length))];
                    _ = await entryStream.ReadAsync(testBuffer, 0, testBuffer.Length).ConfigureAwait(false);

                    return true;
                }
                catch
                {
                    return false;
                }
            }

            // For RAR files, check basic file signature
            if (extension == ".rar")
            {
                fileStream.Position = 0;
                var signature = new byte[7];
                await fileStream.ReadAsync(signature, 0, 7);

                // Check for RAR signature: "Rar!" (0x52 0x61 0x72 0x21)
                return signature.Length >= 4 &&
                       signature[0] == 0x52 && signature[1] == 0x61 &&
                       signature[2] == 0x72 && signature[3] == 0x21;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void PlayPopSound()
    {
        var soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-sound.mp3");

        if (!File.Exists(soundPath))
        {
            SystemSounds.Asterisk.Play();
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var player = new System.Windows.Media.MediaPlayer();
                    player.Open(new Uri(soundPath));
                    player.Volume = 0.7;
                    player.Play();

                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    timer.Tick += (s, e) => { timer.Stop(); player.Close(); };
                    timer.Start();
                });
            }
            catch
            {
                SystemSounds.Asterisk.Play();
            }
        });
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
        jobManagerService = ServiceLocator.GetService<JobManagerService>();
        configService = ServiceLocator.GetService<ConfigService>();
        configService.OnConfigSelected += (_, config) =>
        {
            OnPropertyChanged(nameof(IsConfigSelected));
            ConfigSelected?.Invoke(config);
        };
    }

    public void OnWindowClosing(object sender, CancelEventArgs e)
    {
        var obSettingsService = ServiceLocator.GetService<OpenBulletSettingsService>();

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

    Hits = 13,
    Plugins = 14,
    OBSettings = 15,
    RLSettings = 16,
    About = 17
}
