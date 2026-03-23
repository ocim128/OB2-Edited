using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Flux.Native.Helpers;
using Flux.Native.ViewModels.Settings;

namespace Flux.Native.Views.Pages.Settings;

public partial class RLSettings : Page
{
    private readonly RLSettingsViewModel vm;

    public RLSettings(RLSettingsViewModel vm)
    {
        this.vm = vm;
        DataContext = this.vm;

        InitializeComponent();
    }

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

    private void Reset(object sender, RoutedEventArgs e) => vm.Reset();

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
        if (sender is not Button button)
        {
            return;
        }

        button.IsEnabled = false;
        playwrightInstallProgressRing.Visibility = Visibility.Visible;
        playwrightInstallStatus.Text = "Opening installation window...";

        Window progressWindow = null!;
        ProgressBar progressBar = null!;
        Label statusLabel = null!;
        TextBox logTextBox = null!;
        var installCompleted = false;
        var browserCount = 2;
        var currentBrowserIndex = 0;

        try
        {
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

            var headerLabel = new Label
            {
                Content = "Installing Playwright Browsers",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Modern.ForegroundMain"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            mainPanel.Children.Add(headerLabel);

            progressBar = new ProgressBar
            {
                Margin = new Thickness(0, 10, 0, 10),
                Height = 20,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            mainPanel.Children.Add(progressBar);

            statusLabel = new Label
            {
                Content = "Preparing installation...",
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Modern.ThemeMain"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            mainPanel.Children.Add(statusLabel);

            var logLabel = new Label
            {
                Content = "Installation Log:",
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Modern.ForegroundSecondary"),
                Margin = new Thickness(0, 10, 0, 5)
            };
            mainPanel.Children.Add(logLabel);

            logTextBox = new TextBox
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

            progressWindow.Closing += (_, args) =>
            {
                if (installCompleted)
                {
                    return;
                }

                var result = MessageBox.Show(
                    "Installation is still in progress. Are you sure you want to close this window?\n\nNote: The installation will continue in the background.",
                    "Installation In Progress",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    args.Cancel = true;
                }
            };

            progressWindow.Show();

            await vm.InstallPlaywrightBrowsers(log =>
            {
                Dispatcher.Invoke(() =>
                {
                    playwrightInstallStatus.Text = log;
                    logTextBox.AppendText(log + Environment.NewLine);
                    logTextBox.ScrollToEnd();
                    statusLabel.Content = log;

                    if (log.Contains("Installing ", StringComparison.OrdinalIgnoreCase) ||
                        log.Contains("already installed", StringComparison.OrdinalIgnoreCase))
                    {
                        currentBrowserIndex++;
                        progressBar.Value = (currentBrowserIndex / (double)(browserCount + 1)) * 100;
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
            statusLabel.Content = "All browsers installed successfully!";
            logTextBox.AppendText(Environment.NewLine + "Installation completed successfully!" + Environment.NewLine);
            logTextBox.ScrollToEnd();

            playwrightInstallStatus.Text = "Browsers installed successfully!";
            Alert.Success("Success", "Playwright browsers installed successfully.");

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
                statusLabel.Content = "Installation failed";
                logTextBox.AppendText(Environment.NewLine + $"ERROR: {ex.Message}" + Environment.NewLine);
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
}
