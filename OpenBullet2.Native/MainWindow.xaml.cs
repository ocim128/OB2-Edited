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

        // Lazy initialization - pages created only when needed
        // This reduces initial memory usage and improves startup time

        Title = "OpenBullet 2 - 0.3.3 [akunlama MOD]";

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
            case MainWindowPage.ConfigLoliScript:
                NavigateToConfigLoliScriptPage();
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

    // Consolidated navigation handler - reduced from 18 individual methods
    private void HandleNavigation(object sender, MouseEventArgs e)
    {
        var element = sender as FrameworkElement;
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
            "menuOptionLoliScript" => MainWindowPage.ConfigLoliScript,
            _ => MainWindowPage.Home
        };
        NavigateTo(page);
    }

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
    ConfigLoliScript = 12,
    Hits = 13,
    Plugins = 14,
    OBSettings = 15,
    RLSettings = 16,
    About = 17
}
