using Flux.Core.Services;
using Flux.Native.Extensions;
using Flux.Native.Helpers;
using Flux.Native.Services;
using Flux.Native.ViewModels;
using Flux.Native.ViewModels.Jobs;
using Flux.Native.ViewModels.Data;
using Flux.Native.Views.Dialogs.Common;
using Flux.Native.Views.Dialogs.Job;
using RuriLib.Models.Configs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;


namespace Flux.Native.Views.Pages.Jobs;

/// <summary>
/// Interaction logic for MultiRunJobViewer.xaml
/// </summary>
public partial class MultiRunJobViewer : Page
{
        private readonly MainWindow mainWindow;
        private MultiRunJobViewerViewModel vm;
        private GridViewColumnHeader listViewSortCol;
        private SortAdorner listViewSortAdorner;

        private IEnumerable<HitViewModel> GetSelectedHits() => resultsListView.SelectedItems.Cast<HitViewModel>().ToList();
        private IEnumerable<BotViewModel> GetSelectedBots() => botsListView.SelectedItems.Cast<BotViewModel>().ToList();

        private void BotsListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
            => SelectListViewItemUnderMouse(botsListView, e);

        private void ResultsListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
            => SelectListViewItemUnderMouse(resultsListView, e);

        private void ResultsListView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    CopySelectedHitsCapture(sender, e);
                }
                else
                {
                    CopySelectedHits(sender, e);
                }

                e.Handled = true;
            }
        }

        public MultiRunJobViewer()
        {
            mainWindow = App.ServiceProvider.GetRequiredService<MainWindow>();
            InitializeComponent();
        }

        public void BindViewModel(MultiRunJobViewModel jobVM)
        {
            if (vm is not null)
            {
                vm.Dispose();

                try
                {
                    vm.NewMessage -= OnResultMessage;
                    vm.SparklineDataUpdated -= UpdateSparklines;
                }
                catch
                {
                    // The event might not have been subscribed, so we can ignore this exception.
                }
            }

            vm = new MultiRunJobViewerViewModel(jobVM);
            vm.NewMessage += OnResultMessage;
            vm.SparklineDataUpdated += UpdateSparklines;
            DataContext = vm;

            // Set the initial active tab to Hits
            SetActiveTab("Hits");
            
            // Clear sparklines when binding to a new job
            CpmSparkline?.Clear();
        }
        
        /// <summary>
        /// Updates the sparkline charts with the latest data from the ViewModel.
        /// </summary>
        private void UpdateSparklines()
        {
            Dispatcher.Invoke(() =>
            {
                if (vm != null && CpmSparkline != null)
                {
                    CpmSparkline.SetDataPoints(vm.CpmHistory);
                }
            });
        }

        private async void Start(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.StartAsync(), "starting multi-run job");

        private async void Stop(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.StopAsync(), "stopping multi-run job");

        private async void Pause(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.PauseAsync(), "pausing multi-run job");

        private async void Resume(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.ResumeAsync(), "resuming multi-run job");

        private async void Abort(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.AbortAsync(), "aborting multi-run job");

        private void SkipWait(object sender, RoutedEventArgs e)
            => Alert.SafeExecute(() => vm.SkipWait(), "skipping wait");

        private void ChangeOptions(object sender, RoutedEventArgs e) => mainWindow.EditJob(vm.Job);

        private void ChangeBots(object sender, RoutedEventArgs e)
            => new MainDialog(new ChangeBotsDialog(this, vm.Job.Bots), "Change bots").ShowDialog();

        public async Task ChangeBots(int newValue)
            => await Alert.SafeExecuteAsync(() => vm.ChangeBotsAsync(newValue), "changing bot count");

        private void ResetSkip(object sender, RoutedEventArgs e)
        {
            Alert.SafeExecute(() =>
            {
                var dialog = new ConfirmationDialog(
                    "Reset Skip Confirmation",
                    "Are you sure you want to reset the skip count to 0?\n\nThis will restart the job from the beginning of the data pool.");

                dialog.ShowDialog(Application.Current.MainWindow);

                if (dialog.Result)
                {
                    vm.ResetSkip();
                }
            }, "resetting skip count");
        }

        // Quick bot adjustment handlers
        private async void IncreaseBots1(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.IncreaseBotsByAsync(1), "increasing bots by 1");

        private async void IncreaseBots10(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.IncreaseBotsByAsync(10), "increasing bots by 10");

        private async void DecreaseBots1(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.DecreaseBotsByAsync(1), "decreasing bots by 1");

        private async void DecreaseBots10(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.DecreaseBotsByAsync(10), "decreasing bots by 10");

        // Copy all hits handlers
        private void CopyAllHits(object sender, RoutedEventArgs e)
        {
            var hitsText = vm.GetAllHitsForClipboard();
            if (string.IsNullOrWhiteSpace(hitsText))
            {
                Alert.Warning("No Hits", "There are no hits to copy.");
                return;
            }
            Clipboard.SetText(hitsText);
            var count = hitsText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length;
            Alert.Success("Clipboard", $"Copied {count} hits to clipboard.");
        }

        private void CopyAllHitsWithCapture(object sender, RoutedEventArgs e)
        {
            var hitsText = vm.GetAllHitsWithCaptureForClipboard();
            if (string.IsNullOrWhiteSpace(hitsText))
            {
                Alert.Warning("No Hits", "There are no hits to copy.");
                return;
            }
            Clipboard.SetText(hitsText);
            var count = hitsText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length;
            Alert.Success("Clipboard", $"Copied {count} hits with capture to clipboard.");
        }

        private async void CopySelectedHits(object sender, RoutedEventArgs e)
            => await CopyHitsToClipboardAsync(h => h.Data, "hit", "hits");

        private async void CopySelectedProxies(object sender, RoutedEventArgs e)
            => await CopyHitsToClipboardAsync(h => h.Proxy, "hit proxy", "hit proxies");

        private async void CopySelectedHitsCapture(object sender, RoutedEventArgs e)
            => await CopyHitsToClipboardAsync(h => $"{h.Data} | {h.Capture}", "hit entry with capture", "hit entries with capture");

        private void SendToDebugger(object sender, RoutedEventArgs e)
        {
            var hitVM = GetSelectedHits().FirstOrDefault();

            if (hitVM is not null)
            {
                var debugger = App.ServiceProvider.GetRequiredService<ViewModelsService>().Debugger;
                debugger.TestData = hitVM.Data;

                if (hitVM.Hit.Proxy is not null)
                {
                    debugger.TestProxy = hitVM.Hit.Proxy.ToString();
                    debugger.ProxyType = hitVM.Hit.Proxy.Type;
                }
            }
        }

        private void SelectAll(object sender, RoutedEventArgs e) => resultsListView.SelectAll();

        private void ShowBotLog(object sender, RoutedEventArgs e)
        {
            var hitVM = GetSelectedHits().FirstOrDefault();

            if (hitVM is null) return;

            if (hitVM.Hit.Config.Mode == ConfigMode.DLL)
            {
                Alert.Error("Bot log unavailable", "The bot log is not available for pre-compiled configs");
                return;
            }

            new MainDialog(new BotLogDialog(hitVM.Hit.BotLogger), $"Bot log for {hitVM.Data}", 950, 700).Show();
        }

        private void ColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            var column = sender as GridViewColumnHeader;
            var sortBy = column.Tag.ToString();

            if (listViewSortCol != null)
            {
                AdornerLayer.GetAdornerLayer(listViewSortCol).Remove(listViewSortAdorner);
                botsListView.Items.SortDescriptions.Clear();
            }

            var newDir = ListSortDirection.Ascending;

            if (listViewSortCol == column && listViewSortAdorner.Direction == newDir)
            {
                newDir = ListSortDirection.Descending;
            }

            listViewSortCol = column;
            listViewSortAdorner = new SortAdorner(listViewSortCol, newDir);
            AdornerLayer.GetAdornerLayer(listViewSortCol).Add(listViewSortAdorner);
            botsListView.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));
        }

        private void LVIRightClick(object sender, MouseButtonEventArgs e)
        {
            // This method is intentionally empty. The logic for right-click context menu is handled in XAML.
        }

        private void OnResultMessage(object sender, string message, Color color)
        {
            // Log messages are now handled via right-click menu 
            // on hits list, no longer displayed in main view
        }

        // Tab functionality for filtering hits by type
        private void ShowHitsTab(object sender, RoutedEventArgs e)
        {
            SetActiveTab("Hits");
            vm.HitsFilter = HitsFilter.Hits;
        }

        private void ShowCustomTab(object sender, RoutedEventArgs e)
        {
            SetActiveTab("Custom");
            vm.HitsFilter = HitsFilter.Custom;
        }

        private void ShowToCheckTab(object sender, RoutedEventArgs e)
        {
            SetActiveTab("ToCheck");
            vm.HitsFilter = HitsFilter.ToCheck;
        }

        private void SetActiveTab(string activeTab)
        {
            var inactiveBackground = GetThemeBrush("Modern.BackgroundElevated", Color.FromRgb(0xE2, 0xE8, 0xF0));
            var inactiveForeground = GetThemeBrush("Modern.ForegroundSecondary", Color.FromRgb(0x33, 0x41, 0x55));
            var accentForeground = GetThemeBrush("Modern.TextOnAccent", Colors.White);

            // Reset all tabs to inactive state
            HitsTabButton.Background = inactiveBackground;
            HitsTabButton.Foreground = inactiveForeground;
            CustomTabButton.Background = inactiveBackground;
            CustomTabButton.Foreground = inactiveForeground;
            ToCheckTabButton.Background = inactiveBackground;
            ToCheckTabButton.Foreground = inactiveForeground;

            // Set the active tab
            switch (activeTab)
            {
                case "Hits":
                    HitsTabButton.Background = GetThemeBrush("Modern.Success", Color.FromRgb(0x10, 0xB9, 0x81));
                    HitsTabButton.Foreground = accentForeground;
                    break;
                case "Custom":
                    CustomTabButton.Background = GetThemeBrush("Modern.ThemeAccent", Color.FromRgb(0x8B, 0x5C, 0xF6));
                    CustomTabButton.Foreground = accentForeground;
                    break;
                case "ToCheck":
                    ToCheckTabButton.Background = GetThemeBrush("Modern.Warning", Color.FromRgb(0xF5, 0x9E, 0x0B));
                    ToCheckTabButton.Foreground = accentForeground;
                    break;
            }
        }

        private static SolidColorBrush GetThemeBrush(string key, Color fallback)
        {
            if (Application.Current?.Resources[key] is SolidColorBrush brush)
            {
                return brush;
            }

            return new SolidColorBrush(fallback);
        }

        private static void SelectListViewItemUnderMouse(ListView listView, MouseButtonEventArgs e)
        {
            var container = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
            if (container is null)
            {
                return;
            }

            if (!container.IsSelected)
            {
                listView.SelectedItems.Clear();
                container.IsSelected = true;
            }

            container.Focus();
        }

        private static T? FindAncestor<T>(DependencyObject? current)
            where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        // Bot context menu methods
        private async void CopySelectedBotData(object sender, RoutedEventArgs e)
            => await GetSelectedBots().CopyToClipboardAsync(b => b.Data);

        private async void CopySelectedBotProxy(object sender, RoutedEventArgs e)
            => await GetSelectedBots().CopyToClipboardAsync(b => b.Proxy);

        private async void CopySelectedBotInfo(object sender, RoutedEventArgs e)
            => await GetSelectedBots().CopyToClipboardAsync(b => b.Info ?? "");

        private async Task CopyHitsToClipboardAsync(Func<HitViewModel, string> mapping, string singular, string plural = null)
        {
            var hits = GetSelectedHits().ToList();
            if (!hits.Any())
            {
                return;
            }

            await hits.CopyToClipboardAsync(mapping);
            ShowClipboardNotification(hits.Count, singular, plural);
        }

        private static void ShowClipboardNotification(int count, string singular, string plural = null)
        {
            var label = count == 1 ? singular : plural ?? $"{singular}s";
            Alert.Success("Clipboard", $"Copied {count} {label} to clipboard.");
        }

        private void SelectAllBots(object sender, RoutedEventArgs e) => botsListView.SelectAll();

    }



