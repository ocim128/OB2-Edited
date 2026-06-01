using Flux.Core.Services;
using Flux.Native.Extensions;
using Flux.Native.Helpers;
using Flux.Native.Services;
using Flux.Native.ViewModels;
using Flux.Native.ViewModels.Jobs;
using Flux.Native.ViewModels.Data;
using Flux.Native.ViewModels.Shared;
using Flux.Native.Views.Dialogs.Common;
using Flux.Native.Views.Dialogs.Job;
using Flux.Shared.Abstractions;
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


namespace Flux.Native.Views.Pages.Jobs;

/// <summary>
/// Interaction logic for MultiRunJobViewer.xaml
/// </summary>
public partial class MultiRunJobViewer : Page, IDisposable
{
        private readonly MainWindow mainWindow;
        private readonly FluxSettingsService fluxSettingsService;
        private readonly IJobCommands jobCommands;
        private readonly IJobQueries jobQueries;
        private readonly DebuggerViewModel debuggerViewModel;
        private MultiRunJobViewerViewModel vm;
        private GridViewColumnHeader listViewSortCol;
        private SortAdorner listViewSortAdorner;
        private GridViewColumnHeader resultListViewSortCol;
        private SortAdorner resultListViewSortAdorner;
        private string resultListViewSortBy = string.Empty;
        private ListSortDirection resultListViewSortDir = ListSortDirection.Ascending;
        private DateTime resultListViewLastClickTime = DateTime.MinValue;
        private static readonly TimeSpan ResultHeaderRapidClickInterval = TimeSpan.FromMilliseconds(500);
        private volatile bool disposed;

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

        public MultiRunJobViewer(
            MainWindow mainWindow,
            FluxSettingsService fluxSettingsService,
            IJobCommands jobCommands,
            IJobQueries jobQueries,
            DebuggerViewModel debuggerViewModel)
        {
            this.mainWindow = mainWindow;
            this.fluxSettingsService = fluxSettingsService;
            this.jobCommands = jobCommands;
            this.jobQueries = jobQueries;
            this.debuggerViewModel = debuggerViewModel;
            InitializeComponent();
        }

        public async void BindViewModel(MultiRunJobViewModel jobVM)
        {
            if (disposed)
            {
                return;
            }

            CleanupViewModel();

            var nextViewModel = await MultiRunJobViewerViewModel.CreateAsync(jobVM, fluxSettingsService, jobCommands, jobQueries);
            if (disposed)
            {
                nextViewModel.Dispose();
                return;
            }

            vm = nextViewModel;
            nextViewModel.NewMessage += OnResultMessage;
            nextViewModel.SparklineDataUpdated += UpdateSparklines;
            nextViewModel.PropertyChanged += OnViewModelPropertyChanged;
            DataContext = nextViewModel;

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
            if (disposed)
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (!disposed && vm != null && CpmSparkline != null)
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

        private async void SkipWait(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.SkipWaitAsync(), "skipping wait");

        private void ChangeOptions(object sender, RoutedEventArgs e) => mainWindow.EditJob(vm.Job);

        private void ChangeBots(object sender, RoutedEventArgs e)
            => new MainDialog(new ChangeBotsDialog(this, vm.Job.Bots), "Change bots").ShowDialog();

        public async Task ChangeBots(int newValue)
            => await Alert.SafeExecuteAsync(() => vm.ChangeBotsAsync(newValue), "changing bot count");

        private async void ResetSkip(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ConfirmationDialog(
                    "Reset Skip Confirmation",
                    "Are you sure you want to reset the skip count to 0?\n\nThis will restart the job from the beginning of the data pool.");

                dialog.ShowDialog(Application.Current.MainWindow);

                if (dialog.Result)
                {
                    await Alert.SafeExecuteAsync(() => vm.ResetSkipAsync(), "resetting skip count");
                }
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
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
                debuggerViewModel.TestData = hitVM.Data;

                if (!string.IsNullOrWhiteSpace(hitVM.Proxy))
                {
                    debuggerViewModel.TestProxy = hitVM.Proxy;
                }

                if (hitVM.ProxyType.HasValue)
                {
                    debuggerViewModel.ProxyType = hitVM.ProxyType.Value;
                }
            }
        }

        private void SelectAll(object sender, RoutedEventArgs e) => resultsListView.SelectAll();

        private async void ShowBotLog(object sender, RoutedEventArgs e)
        {
            var hitVM = GetSelectedHits().FirstOrDefault();

            if (hitVM is null) return;

            if (hitVM.ConfigMode == ConfigMode.DLL)
            {
                Alert.Error("Bot log unavailable", "The bot log is not available for pre-compiled configs");
                return;
            }

            var botLog = await vm.GetBotLogAsync(hitVM.ResultId);
            new MainDialog(new BotLogDialog(botLog), $"Bot log for {hitVM.Data}", 950, 700).Show();
        }

        private void ColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            var column = sender as GridViewColumnHeader;
            if (column?.Tag is not string sortBy) return;

            if (listViewSortCol != null)
            {
                var oldLayer = AdornerLayer.GetAdornerLayer(listViewSortCol);
                if (oldLayer != null) oldLayer.Remove(listViewSortAdorner);
                botsListView.Items.SortDescriptions.Clear();
            }

            var newDir = ListSortDirection.Ascending;

            if (listViewSortCol == column && listViewSortAdorner.Direction == newDir)
            {
                newDir = ListSortDirection.Descending;
            }

            listViewSortCol = column;
            listViewSortAdorner = new SortAdorner(listViewSortCol, newDir);
            var layer = AdornerLayer.GetAdornerLayer(listViewSortCol);
            if (layer != null) layer.Add(listViewSortAdorner);
            botsListView.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));
        }

        private void ResultsColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            var column = sender as GridViewColumnHeader;
            if (column?.Tag is not string sortBy) return;

            var now = DateTime.UtcNow;
            var isRapidRepeatClick = resultListViewSortCol == column &&
                now - resultListViewLastClickTime <= ResultHeaderRapidClickInterval;
            resultListViewLastClickTime = now;

            var newDir = ListSortDirection.Ascending;
            if (!isRapidRepeatClick && resultListViewSortCol == column && resultListViewSortAdorner?.Direction == newDir)
            {
                newDir = ListSortDirection.Descending;
            }

            ApplyResultsSort(column, sortBy, newDir);
        }

        private void ApplyResultsSort(GridViewColumnHeader column, string sortBy, ListSortDirection direction)
        {
            if (resultListViewSortCol != null)
            {
                var oldLayer = AdornerLayer.GetAdornerLayer(resultListViewSortCol);
                if (oldLayer != null) oldLayer.Remove(resultListViewSortAdorner);
            }

            resultListViewSortCol = column;
            resultListViewSortAdorner = new SortAdorner(resultListViewSortCol, direction);
            resultListViewSortBy = sortBy;
            resultListViewSortDir = direction;

            var layer = AdornerLayer.GetAdornerLayer(resultListViewSortCol);
            if (layer != null) layer.Add(resultListViewSortAdorner);
            ApplyResultSortDescription();
        }

        private void ApplyResultSortDescription()
        {
            if (string.IsNullOrWhiteSpace(resultListViewSortBy))
            {
                return;
            }

            resultsListView.Items.SortDescriptions.Clear();
            resultsListView.Items.SortDescriptions.Add(new SortDescription(resultListViewSortBy, resultListViewSortDir));
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (disposed || e.PropertyName != nameof(MultiRunJobViewerViewModel.HitsCollection))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!disposed)
                {
                    ApplyResultSortDescription();
                }
            }));
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
            if (Application.Current?.Resources[key] is SolidColorBrush existingBrush)
            {
                return existingBrush;
            }

            var fallbackBrush = new SolidColorBrush(fallback);
            fallbackBrush.Freeze();
            return fallbackBrush;
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

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            CleanupViewModel();
        }

        private void CleanupViewModel()
        {
            if (vm is null)
            {
                DataContext = null;
                return;
            }

            vm.NewMessage -= OnResultMessage;
            vm.SparklineDataUpdated -= UpdateSparklines;
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.Dispose();
            vm = null;
            DataContext = null;
        }

    }
