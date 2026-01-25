using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.ViewModels.Settings;
using RuriLib.Functions.Captchas;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;


namespace OpenBullet2.Native.Views.Pages.Settings;

/// <summary>
/// Interaction logic for RLSettings.xaml
/// </summary>
public partial class RLSettings : Page
{
    private readonly RLSettingsViewModel vm;

    public RLSettings()
    {
        vm = App.ServiceProvider.GetRequiredService<ViewModelsService>().RLSettings;
        vm.CaptchaServiceChanged += UpdateCaptchaTabControl;
        DataContext = vm;

        InitializeComponent();

        UpdateCaptchaTabControl(vm.CurrentCaptchaService);
        SetMultiLineTextBoxContents();
    }

    private void CustomUserAgentsChanged(object sender, TextChangedEventArgs e)
        => vm.UserAgents = customUserAgentsListTextBox.Text.Split(Environment.NewLine).ToList();

    private void GlobalBanKeysChanged(object sender, TextChangedEventArgs e)
        => vm.GlobalBanKeys = globalBanKeysTextBox.Text.Split(Environment.NewLine).ToList();

    private void GlobalRetryKeysChanged(object sender, TextChangedEventArgs e)
        => vm.GlobalRetryKeys = globalRetryKeysTextBox.Text.Split(Environment.NewLine).ToList();

    private async void Save(object sender, RoutedEventArgs e)
    {
        try
        {
            await vm.Save();
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }

    private void Reset(object sender, RoutedEventArgs e)
    {
        vm.Reset();
        SetMultiLineTextBoxContents();
    }

    private async void CheckCaptchaBalance(object sender, RoutedEventArgs e)
    {
        try
        {
            var balance = await vm.CheckCaptchaBalance();
            Alert.Success("Success", $"Balance: {balance}");
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }

    private async void InstallPlaywrightBrowsers(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null) return;
        
        button.IsEnabled = false;
        playwrightInstallProgressRing.Visibility = Visibility.Visible;
        playwrightInstallStatus.Text = "Opening installation window...";

        // Create a progress dialog window similar to the Update flow
        Window progressWindow = null!;
        System.Windows.Controls.ProgressBar progressBar = null!;
        System.Windows.Controls.Label statusLabel = null!;
        System.Windows.Controls.TextBox logTextBox = null!;
        var installCompleted = false;
        var browserCount = 2; // Chromium, Firefox
        var currentBrowserIndex = 0;

        try
        {
            // Create the progress window on the UI thread
            progressWindow = new Window
            {
                Title = "Installing Playwright Browsers",
                Width = 550,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = true,
                Topmost = true,
                Background = (System.Windows.Media.Brush)Application.Current.FindResource("Modern.BackgroundSecondary"),
                WindowStyle = WindowStyle.ToolWindow
            };

            var mainPanel = new StackPanel { Margin = new Thickness(20) };

            // Header
            var headerLabel = new System.Windows.Controls.Label
            {
                Content = "🎭 Installing Playwright Browsers",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Modern.ForegroundMain"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            mainPanel.Children.Add(headerLabel);

            // Progress bar
            progressBar = new System.Windows.Controls.ProgressBar
            {
                Margin = new Thickness(0, 10, 0, 10),
                Height = 20,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            mainPanel.Children.Add(progressBar);

            // Status label
            statusLabel = new System.Windows.Controls.Label
            {
                Content = "Preparing installation...",
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Modern.ThemeMain"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            mainPanel.Children.Add(statusLabel);

            // Log TextBox (scrollable)
            var logLabel = new System.Windows.Controls.Label
            {
                Content = "Installation Log:",
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Modern.ForegroundSecondary"),
                Margin = new Thickness(0, 10, 0, 5)
            };
            mainPanel.Children.Add(logLabel);

            logTextBox = new System.Windows.Controls.TextBox
            {
                Height = 180,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                Background = (System.Windows.Media.Brush)Application.Current.FindResource("Modern.BackgroundMain"),
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Modern.ForegroundMain"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8)
            };
            mainPanel.Children.Add(logTextBox);

            progressWindow.Content = mainPanel;

            // Handle window closing during installation
            progressWindow.Closing += (_, args) =>
            {
                if (!installCompleted)
                {
                    var result = MessageBox.Show(
                        "Installation is still in progress. Are you sure you want to close this window?\n\n" +
                        "Note: The installation will continue in the background.",
                        "Installation In Progress",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                    {
                        args.Cancel = true;
                    }
                }
            };

            progressWindow.Show();

            // Start installation with progress updates
            await vm.InstallPlaywrightBrowsers(log => 
            {
                Dispatcher.Invoke(() =>
                {
                    // Update the main status in RLSettings page
                    playwrightInstallStatus.Text = log;

                    // Append to log textbox
                    logTextBox.AppendText(log + Environment.NewLine);
                    logTextBox.ScrollToEnd();

                    // Update status label
                    statusLabel.Content = log;

                    // Track browser installation progress
                    if (log.Contains("Installing ", StringComparison.OrdinalIgnoreCase))
                    {
                        currentBrowserIndex++;
                        var progressValue = (currentBrowserIndex / (double)(browserCount + 1)) * 100;
                        progressBar.Value = progressValue;
                    }
                    else if (log.Contains("already installed", StringComparison.OrdinalIgnoreCase))
                    {
                        currentBrowserIndex++;
                        var progressValue = (currentBrowserIndex / (double)(browserCount + 1)) * 100;
                        progressBar.Value = progressValue;
                        logTextBox.AppendText("  ✓ Skipped (already installed)" + Environment.NewLine);
                    }
                    else if (log.Contains("successfully", StringComparison.OrdinalIgnoreCase) || 
                             log.Contains("completed", StringComparison.OrdinalIgnoreCase))
                    {
                        progressBar.Value = 100;
                    }
                });
            });

            installCompleted = true;
            progressBar.Value = 100;
            statusLabel.Content = "✅ All browsers installed successfully!";
            logTextBox.AppendText(Environment.NewLine + "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" + Environment.NewLine);
            logTextBox.AppendText("Installation completed successfully!" + Environment.NewLine);
            logTextBox.ScrollToEnd();

            playwrightInstallStatus.Text = "Browsers installed successfully!";
            Alert.Success("Success", "Playwright browsers installed successfully.");

            // Auto-close after a short delay
            await Task.Delay(2000);
            if (progressWindow.IsVisible)
            {
                progressWindow.Close();
            }
        }
        catch (Exception ex)
        {
            installCompleted = true;
            playwrightInstallStatus.Text = $"Error: {ex.Message}";

            if (progressWindow != null && statusLabel != null && logTextBox != null)
            {
                statusLabel.Content = "❌ Installation failed";
                logTextBox.AppendText(Environment.NewLine + "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" + Environment.NewLine);
                logTextBox.AppendText($"ERROR: {ex.Message}" + Environment.NewLine);
                logTextBox.ScrollToEnd();
            }

            Alert.Exception(ex);
        }
        finally
        {
            button.IsEnabled = true;
            playwrightInstallProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateCaptchaTabControl(CaptchaServiceType service)
    {
        var values = Enum.GetValues(typeof(CaptchaServiceType)).Cast<CaptchaServiceType>().ToList();
        var index = values.IndexOf(service);
        captchaServiceTabControl.SelectedIndex = index;
    }

    private void SetMultiLineTextBoxContents()
    {
        customUserAgentsListTextBox.Text = string.Join(Environment.NewLine, vm.UserAgents);
        globalBanKeysTextBox.Text = string.Join(Environment.NewLine, vm.GlobalBanKeys);
        globalRetryKeysTextBox.Text = string.Join(Environment.NewLine, vm.GlobalRetryKeys);
    }
}


