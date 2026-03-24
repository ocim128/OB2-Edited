using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Flux.Native.ViewModels.Pages;

namespace Flux.Native.Views.Pages.Tools;

public partial class Monitor : Page
{
    private const double CardMinWidth = 300;
    private const double CardMaxWidth = 420;
    private const double CardHorizontalSpacing = 16;
    private const int CardMaxColumns = 3;

    private readonly ToolsPageViewModel viewModel;

    public Monitor()
    {
        InitializeComponent();

        viewModel = new ToolsPageViewModel();
        DataContext = viewModel;
        Unloaded += Monitor_Unloaded;
    }

    private async void Monitor_Unloaded(object sender, RoutedEventArgs e)
    {
        await viewModel.CleanupAsync();
    }

    private void NavigateToToolCard(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var alias = button.Tag as string ?? button.Content as string ?? string.Empty;
        var targetCard = GetToolElement(alias);

        if (targetCard is null)
        {
            return;
        }

        if (targetCard.Visibility != Visibility.Visible)
        {
            viewModel.ResetFilters();
        }

        Dispatcher.InvokeAsync(() =>
        {
            targetCard.BringIntoView();
            targetCard.Focus();
        }, DispatcherPriority.Background);
    }

    private FrameworkElement? GetToolElement(string alias)
    {
        var tool = viewModel.GetToolByAlias(alias);
        if (tool is null)
        {
            return null;
        }

        return tool.Title switch
        {
            "OTP Toolkit" => OtpToolCard,
            "Bookmarklet Parser" => BookmarkletToolCard,
            "Text Cleaner" => TextCleanerToolCard,
            "Firefox Switcher" => FirefoxToolCard,
            "Line Reducer" => LineReducerToolCard,
            _ => null
        };
    }

    private void ToolsScrollViewer_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            UpdateCardLayout(element.ActualWidth);
        }
    }

    private void ToolsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCardLayout(e.NewSize.Width);
    }

    private void UpdateCardLayout(double availableWidth)
    {
        if (ToolCardPanel == null || double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            return;
        }

        availableWidth = Math.Max(availableWidth - SystemParameters.VerticalScrollBarWidth, CardMinWidth);

        var maxColumns = Math.Max(1, (int)Math.Floor((availableWidth + CardHorizontalSpacing) / (CardMinWidth + CardHorizontalSpacing)));
        maxColumns = Math.Min(maxColumns, CardMaxColumns);

        for (var columns = maxColumns; columns >= 1; columns--)
        {
            var candidate = (availableWidth - (columns - 1) * CardHorizontalSpacing) / columns;
            if (candidate < CardMinWidth)
            {
                continue;
            }

            ApplyCardWidth(Math.Min(candidate, CardMaxWidth));
            return;
        }

        ApplyCardWidth(Math.Max(CardMinWidth, Math.Min(CardMaxWidth, availableWidth)));
    }

    private void ApplyCardWidth(double width)
    {
        if (double.IsNaN(width) || width <= 0 || ToolCardPanel == null)
        {
            return;
        }

        if (Math.Abs(ToolCardPanel.ItemWidth - width) > 0.5)
        {
            ToolCardPanel.ItemWidth = width;
        }
    }
}
