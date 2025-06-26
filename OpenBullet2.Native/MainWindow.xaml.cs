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

namespace OpenBullet2.Native
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private readonly UpdateService updateService;
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

        // Responsive design properties
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
            CommandBindings.Add(new CommandBinding(CustomCommands.NewConfig, OnNewConfigExecuted, OnCanExecuteConfigCommand));
            CommandBindings.Add(new CommandBinding(CustomCommands.OpenConfig, OnOpenConfigExecuted, OnCanExecuteConfigCommand));
            CommandBindings.Add(new CommandBinding(CustomCommands.SaveConfig, OnSaveConfigExecuted, OnCanExecuteConfigCommand));
            CommandBindings.Add(new CommandBinding(CustomCommands.Refresh, OnRefreshExecuted, OnCanExecuteRefreshCommand));
            CommandBindings.Add(new CommandBinding(CustomCommands.Quit, OnQuitExecuted));
            CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToHome, OnNavigateToHomeExecuted));
            CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToJobs, OnNavigateToJobsExecuted));
            CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToMonitor, OnNavigateToMonitorExecuted));
            CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToProxies, OnNavigateToProxiesExecuted));
            CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToWordlists, OnNavigateToWordlistsExecuted));
            CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToConfigs, OnNavigateToConfigsExecuted));
            CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToHits, OnNavigateToHitsExecuted));
            CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToPlugins, OnNavigateToPluginsExecuted));
            CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToOBSettings, OnNavigateToOBSettingsExecuted));
            CommandBindings.Add(new CommandBinding(CustomCommands.NavigateToRLSettings, OnNavigateToRLSettingsExecuted));

            labels = new TextBlock[]
            {
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
            };

            // Pages to initialize as soon as the program starts. This is done to reduce the loading time
            // when clicking on them, as it can be frustrating for the user on specific pages.
            configsPage = new();

            updateService = SP.GetService<UpdateService>();
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
                    double marginSize = Math.Max(8, Math.Min(24, currentWidth * 0.015));
                    
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

        private Thickness CreateThickness(string value)
        {
            try
            {
                var parts = value.Split(',');
                if (parts.Length == 2)
                {
                    var horizontal = double.Parse(parts[0]);
                    var vertical = double.Parse(parts[1]);
                    return new Thickness(horizontal, vertical, horizontal, vertical);
                }
                return new Thickness(0);
            }
            catch
            {
                return new Thickness(0);
            }
        }

        #endregion

        public async void NavigateTo(MainWindowPage page)
        {
            vm.IsLoading = true;

            // Needed to save the content of the LoliCode editor when changing page
            if (CurrentPage == configEditorPage)
            {
                configEditorPage?.OnPageChanged();
            }

            try
            {
                // For Jobs page, navigate directly without Task.Run to avoid threading issues
                if (page == MainWindowPage.Jobs)
                {
                    System.Diagnostics.Debug.WriteLine("Direct Jobs navigation");
                    if (jobsPage is null) 
                    {
                        System.Diagnostics.Debug.WriteLine("Creating new Jobs page directly");
                        jobsPage = new();
                        System.Diagnostics.Debug.WriteLine("Jobs page created successfully");
                    }
                    ChangePage(jobsPage, menuOptionJobs);
                    vm.IsLoading = false;
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Direct Jobs navigation error: {ex.Message}");
                Alert.Exception(ex);
                vm.IsLoading = false;
                return;
            }

            // Simulate async loading to show the indicator for other pages
            await Task.Run(() =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    switch (page)
                    {
                        case MainWindowPage.Home:
                            homePage = new Home(); // We recreate the homepage each time to display updated announcements
                            ChangePage(homePage, menuOptionHome);
                            break;



                        case MainWindowPage.Monitor:
                            if (monitorPage is null) monitorPage = new();
                            ChangePage(monitorPage, menuOptionMonitor);
                            break;

                        case MainWindowPage.Proxies:
                            if (proxiesPage is null) proxiesPage = new();
                            proxiesPage.UpdateViewModel();
                            ChangePage(proxiesPage, menuOptionProxies);
                            break;

                        case MainWindowPage.Wordlists:
                            if (wordlistsPage is null) wordlistsPage = new();
                            ChangePage(wordlistsPage, menuOptionWordlists);
                            break;

                        case MainWindowPage.Configs:
                            if (configsPage is null) configsPage = new();
                            configsPage.UpdateViewModel();
                            ChangePage(configsPage, menuOptionConfigs);
                            break;

                        case MainWindowPage.Hits:
                            if (hitsPage is null) hitsPage = new();
                            hitsPage.UpdateViewModel();
                            ChangePage(hitsPage, menuOptionHits);
                            break;

                        case MainWindowPage.Plugins:
                            if (pluginsPage is null) pluginsPage = new();
                            ChangePage(pluginsPage, menuOptionPlugins);
                            break;

                        case MainWindowPage.OBSettings:
                            if (obSettingsPage is null) obSettingsPage = new();
                            ChangePage(obSettingsPage, menuOptionSettings);
                            break;

                        case MainWindowPage.RLSettings:
                            if (rlSettingsPage is null) rlSettingsPage = new();
                            ChangePage(rlSettingsPage, menuOptionRLSettings);
                            break;

                        case MainWindowPage.About:
                            if (aboutPage is null) aboutPage = new();
                            ChangePage(aboutPage, menuOptionAbout);
                            break;

                        // Initialize config pages when we click on them because a user might not even load them
                        // so we save some RAM (especially the heavy ones that involve a WebBrowser control)

                        case MainWindowPage.ConfigMetadata:
                            CloseSubmenu();
                            if (configMetadataPage is null) configMetadataPage = new();
                            configMetadataPage.UpdateViewModel();
                            ChangePage(configMetadataPage, menuOptionMetadata);
                            break;

                        case MainWindowPage.ConfigReadme:
                            CloseSubmenu();
                            if (configReadmePage is null) configReadmePage = new();
                            configReadmePage.UpdateViewModel();
                            ChangePage(configReadmePage, menuOptionReadme);
                            break;

                        case MainWindowPage.ConfigStacker:

                            if (vm.Config.Mode is not ConfigMode.Stack and not ConfigMode.LoliCode)
                            {
                                return;
                            }

                            CloseSubmenu();
                            if (configEditorPage is null) configEditorPage = new();
                            configEditorPage.NavigateTo(ConfigEditorSection.Stacker);
                            ChangePage(configEditorPage, menuOptionStacker);
                            break;

                        case MainWindowPage.ConfigLoliCode:

                            if (vm.Config.Mode is not ConfigMode.Stack and not ConfigMode.LoliCode)
                            {
                                return;
                            }

                            CloseSubmenu();
                            if (configEditorPage is null) configEditorPage = new();
                            configEditorPage.NavigateTo(ConfigEditorSection.LoliCode);
                            ChangePage(configEditorPage, menuOptionLoliCode);
                            break;

                        case MainWindowPage.ConfigSettings:
                            CloseSubmenu();
                            if (configSettingsPage is null) configSettingsPage = new();
                            configSettingsPage.UpdateViewModel();
                            ChangePage(configSettingsPage, menuOptionConfigSettings);
                            break;

                        case MainWindowPage.ConfigCSharpCode:

                            if (vm.Config.Mode is not ConfigMode.Stack and not ConfigMode.LoliCode and not ConfigMode.CSharp)
                            {
                                return;
                            }

                            CloseSubmenu();
                            if (configEditorPage is null) configEditorPage = new();
                            configEditorPage.NavigateTo(ConfigEditorSection.CSharp);
                            ChangePage(configEditorPage, menuOptionCSharpCode);
                            break;

                        case MainWindowPage.ConfigLoliScript:

                            if (vm.Config.Mode is not ConfigMode.Legacy)
                            {
                                return;
                            }

                            CloseSubmenu();
                            if (configEditorPage is null) configEditorPage = new();
                            configEditorPage.NavigateTo(ConfigEditorSection.LoliScript);
                            ChangePage(configEditorPage, menuOptionLoliScript);
                            break;
                    }
                });
            });

            vm.IsLoading = false;
        }

        public void DisplayJob(JobViewModel jobVM)
        {
            switch (jobVM)
            {
                case MultiRunJobViewModel mrj:
                    if (multiRunJobViewerPage is null) multiRunJobViewerPage = new();
                    multiRunJobViewerPage.BindViewModel(mrj);
                    ChangePage(multiRunJobViewerPage, null);
                    break;

                case ProxyCheckJobViewModel pcj:
                    if (proxyCheckJobViewerPage is null) proxyCheckJobViewerPage = new();
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

        private void OnCanExecuteConfigCommand(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = CurrentPage == configsPage || 
                          CurrentPage == configEditorPage ||
                          CurrentPage == configMetadataPage ||
                          CurrentPage == configReadmePage ||
                          CurrentPage == configSettingsPage;
        }

        private void OnNewConfigExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            configsPage.Create(null, null);
        }

        private void OnOpenConfigExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            configsPage.Edit(null, null);
        }

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
                        Task.Run(async () =>
                        {
                            try
                            {
                                await configRepo.SaveAsync(configService.SelectedConfig);
                                configService.SelectedConfig.UpdateHashes();
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    Alert.Success("Saved", $"{configService.SelectedConfig.Metadata.Name} was saved successfully!");
                                });
                            }
                            catch (Exception ex)
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    Alert.Exception(ex);
                                });
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

        private void OnCanExecuteRefreshCommand(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = CurrentPage == configsPage ||
                           CurrentPage == hitsPage ||
                           CurrentPage == proxiesPage ||
                           CurrentPage == wordlistsPage ||
                           CurrentPage == pluginsPage;
        }

        private void OnQuitExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void OnNavigateToHomeExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            NavigateTo(MainWindowPage.Home);
        }

        private void OnNavigateToJobsExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            NavigateTo(MainWindowPage.Jobs);
        }

        private void OnNavigateToMonitorExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            NavigateTo(MainWindowPage.Monitor);
        }

        private void OnNavigateToProxiesExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            NavigateTo(MainWindowPage.Proxies);
        }

        private void OnNavigateToWordlistsExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            NavigateTo(MainWindowPage.Wordlists);
        }

        private void OnNavigateToConfigsExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            NavigateTo(MainWindowPage.Configs);
        }

        private void OnNavigateToHitsExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            NavigateTo(MainWindowPage.Hits);
        }

        private void OnNavigateToPluginsExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            NavigateTo(MainWindowPage.Plugins);
        }

        private void OnNavigateToOBSettingsExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            NavigateTo(MainWindowPage.OBSettings);
        }

        private void OnNavigateToRLSettingsExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            NavigateTo(MainWindowPage.RLSettings);
        }

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
                Task.Run(async () =>
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

        private void MinimizeWindow(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

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

        private void CloseWindow(object sender, RoutedEventArgs e)
        {
            Close();
        }

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
        #endregion

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
            if (File.Exists(customization.BackgroundImagePath))
            {
                Background = new System.Windows.Media.ImageBrush(
                    new System.Windows.Media.Imaging.BitmapImage(
                        new Uri(customization.BackgroundImagePath)))
                {
                    Opacity = customization.BackgroundOpacity / 100,
                    Stretch = System.Windows.Media.Stretch.UniformToFill
                };
            }
            else
            {
                Background = Brush.Get("BackgroundMain");
            }
        }
    }

    public class MainWindowViewModel : ViewModelBase
    {
        private readonly OpenBulletSettingsService obSettingsService;
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
            obSettingsService = SP.GetService<OpenBulletSettingsService>();
            jobManagerService = SP.GetService<JobManagerService>();
            configService = SP.GetService<ConfigService>();
            configService.OnConfigSelected += (sender, config) =>
            {
                OnPropertyChanged(nameof(IsConfigSelected));
                ConfigSelected?.Invoke(config);
            };
        }

        public void OnWindowClosing(object sender, CancelEventArgs e)
        {
            var obSettingsService = SP.GetService<OpenBulletSettingsService>();

            // Check if the config was saved
            if (obSettingsService.Settings.GeneralSettings.WarnConfigNotSaved && Config != null && Config.HasUnsavedChanges())
            {
                e.Cancel = !Alert.Confirm("Config not saved", $"The config you are editing ({Config.Metadata.Name}) has unsaved changes, are you sure you want to quit?", nameof(obSettingsService.Settings.GeneralSettings.WarnConfigNotSaved));
            }

            // Check if there are jobs running
            if (!e.Cancel && jobManagerService.Jobs.Any(j => j.Status != JobStatus.Idle))
            {
                e.Cancel = !Alert.Confirm("Job(s) running", "One or more jobs are still running, are you sure you want to quit?", "PerformConfirmationOnDestructiveActions");
            }
        }
    }

    public enum MainWindowPage
    {
        Home,
        Jobs,
        Monitor,
        Proxies,
        Wordlists,
        Configs,
        ConfigMetadata,
        ConfigReadme,
        ConfigStacker,
        ConfigLoliCode,
        ConfigSettings,
        ConfigCSharpCode,
        ConfigLoliScript,
        Hits,
        Plugins,
        OBSettings,
        RLSettings,
        About
    }
}
