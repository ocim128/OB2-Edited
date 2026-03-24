using System;
using System.Windows;
using System.Windows.Controls;

namespace Flux.Native.Services;

internal sealed class WpfUpdateProgress : IUpdateProgress
{
    private readonly System.Windows.Window window;
    private readonly ProgressBar progressBar;
    private readonly Label statusLabel;
    private readonly Action onCancel;

    public bool IsVisible => window.IsVisible;

    public WpfUpdateProgress(Action onCancel)
    {
        this.onCancel = onCancel;

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

        window = new System.Windows.Window
        {
            Title = "Downloading Update",
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Content = stackPanel
        };

        window.Closing += (_, _) => this.onCancel();
    }

    public void Show() => window.Show();

    public void Report(double percent, string message)
    {
        if (window.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        window.Dispatcher.Invoke(() =>
        {
            progressBar.IsIndeterminate = false;
            progressBar.Value = percent;
            statusLabel.Content = message;
        });
    }

    public void SetIndeterminate(bool isIndeterminate)
    {
        if (window.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        window.Dispatcher.Invoke(() => progressBar.IsIndeterminate = isIndeterminate);
    }

    public void Close()
    {
        if (window.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        window.Dispatcher.Invoke(() => window.Close());
    }
}
