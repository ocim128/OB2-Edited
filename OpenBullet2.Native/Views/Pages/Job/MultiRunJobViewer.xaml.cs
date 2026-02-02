using OpenBullet2.Core.Services;
using OpenBullet2.Native.Extensions;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.ViewModels.Jobs;
using OpenBullet2.Native.ViewModels.Data;
using OpenBullet2.Native.Views.Dialogs.Common;
using OpenBullet2.Native.Views.Dialogs.Job;
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


namespace OpenBullet2.Native.Views.Pages.Jobs;

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
            // Reset all tabs to inactive state
            HitsTabButton.Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
            HitsTabButton.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            CustomTabButton.Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
            CustomTabButton.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            ToCheckTabButton.Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
            ToCheckTabButton.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

            // Set the active tab
            switch (activeTab)
            {
                case "Hits":
                    HitsTabButton.Background = new SolidColorBrush(Color.FromRgb(0x28, 0xA7, 0x45));
                    HitsTabButton.Foreground = Brushes.White;
                    break;
                case "Custom":
                    CustomTabButton.Background = new SolidColorBrush(Color.FromRgb(0x6F, 0x42, 0xC1));
                    CustomTabButton.Foreground = Brushes.White;
                    break;
                case "ToCheck":
                    ToCheckTabButton.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
                    ToCheckTabButton.Foreground = Brushes.White;
                    break;
            }
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



