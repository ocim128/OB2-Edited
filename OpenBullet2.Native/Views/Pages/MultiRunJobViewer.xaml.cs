using OpenBullet2.Core.Services;
using OpenBullet2.Native.Extensions;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.Views.Dialogs;
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

namespace OpenBullet2.Native.Views.Pages
{
    /// <summary>
    /// Interaction logic for MultiRunJobViewer.xaml
    /// </summary>
    public partial class MultiRunJobViewer : Page
    {
        private readonly MainWindow mainWindow;
        private MultiRunJobViewerViewModel vm;

        private IEnumerable<HitViewModel> GetSelectedHits() => resultsListView.SelectedItems.Cast<HitViewModel>();
        private IEnumerable<BotViewModel> GetSelectedBots() => botsListView.SelectedItems.Cast<BotViewModel>();

        public MultiRunJobViewer()
        {
            mainWindow = SP.GetService<MainWindow>();
            InitializeComponent();
        }

        public void BindViewModel(MultiRunJobViewModel jobVM)
        {
            CleanupViewModel();
            vm = new MultiRunJobViewerViewModel(jobVM);
            vm.NewMessage += OnResultMessage;
            DataContext = vm;
            SetActiveTab("Hits");
        }

        private void CleanupViewModel()
        {
            if (vm is null) return;

            vm.Dispose();
            try { vm.NewMessage -= OnResultMessage; } catch { /* Ignore if not subscribed */ }
        }

        private async void Start(object sender, RoutedEventArgs e) => await ExecuteSafely(() => vm.StartAsync());
        private async void Stop(object sender, RoutedEventArgs e) => await ExecuteSafely(() => vm.StopAsync());
        private async void Pause(object sender, RoutedEventArgs e) => await ExecuteSafely(() => vm.PauseAsync());
        private async void Resume(object sender, RoutedEventArgs e) => await ExecuteSafely(() => vm.ResumeAsync());
        private async void Abort(object sender, RoutedEventArgs e) => await ExecuteSafely(() => vm.AbortAsync());
        private void SkipWait(object sender, RoutedEventArgs e) => ExecuteSafely(() => vm.SkipWait());
        private void ChangeOptions(object sender, RoutedEventArgs e) => mainWindow.EditJob(vm.Job);

        private void ChangeBots(object sender, MouseButtonEventArgs e)
            => new MainDialog(new ChangeBotsDialog(this, vm.Job.Bots), "Change bots").ShowDialog();

        public async Task ChangeBots(int newValue) => await ExecuteSafely(() => vm.ChangeBotsAsync(newValue));

        private void ResetSkip(object sender, MouseButtonEventArgs e)
        {
            var dialog = new ConfirmationDialog(
                "Reset Skip Confirmation",
                "Are you sure you want to reset the skip count to 0?\n\nThis will restart the job from the beginning of the data pool.");

            dialog.ShowDialog(Application.Current.MainWindow);
            if (dialog.Result) ExecuteSafely(() => vm.ResetSkip());
        }

        private void CopySelectedHits(object sender, RoutedEventArgs e) => GetSelectedHits().CopyToClipboard(h => h.Data);
        private void CopySelectedProxies(object sender, RoutedEventArgs e) => GetSelectedHits().CopyToClipboard(h => h.Proxy);
        private void CopySelectedHitsCapture(object sender, RoutedEventArgs e) => GetSelectedHits().CopyToClipboard(h => $"{h.Data} | {h.Capture}");
        private void SelectAll(object sender, RoutedEventArgs e) => resultsListView.SelectAll();

        private void SendToDebugger(object sender, RoutedEventArgs e)
        {
            var hitVM = GetSelectedHits().FirstOrDefault();
            if (hitVM is null) return;

            var debugger = SP.GetService<ViewModelsService>().Debugger;
            debugger.TestData = hitVM.Data;

            if (hitVM.Hit.Proxy is null) return;
            debugger.TestProxy = hitVM.Hit.Proxy.ToString();
            debugger.ProxyType = hitVM.Hit.Proxy.Type;
        }

        private void ShowBotLog(object sender, RoutedEventArgs e)
        {
            var hitVM = GetSelectedHits().FirstOrDefault();
            if (hitVM is null) return;

            if (hitVM.Hit.Config.Mode == ConfigMode.DLL)
            {
                Alert.Error("Bot log unavailable", "The bot log is not available for pre-compiled configs");
                return;
            }

            new MainDialog(new BotLogDialog(hitVM.Hit.BotLogger), $"Bot log for {hitVM.Data}").Show();
        }

        private void ColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not GridViewColumnHeader column || column.Tag?.ToString() is not string sortBy) return;

            botsListView.Items.SortDescriptions.Clear();
            botsListView.Items.SortDescriptions.Add(new SortDescription(sortBy, ListSortDirection.Ascending));
        }

        private void OnResultMessage(object sender, string message, Color color) { /* Handled via context menu */ }

        private void ShowHitsTab(object sender, RoutedEventArgs e) => SetFilterTab("Hits", ViewModels.HitsFilter.Hits);
        private void ShowCustomTab(object sender, RoutedEventArgs e) => SetFilterTab("Custom", ViewModels.HitsFilter.Custom);
        private void ShowToCheckTab(object sender, RoutedEventArgs e) => SetFilterTab("ToCheck", ViewModels.HitsFilter.ToCheck);

        private void SetFilterTab(string tabName, ViewModels.HitsFilter filter)
        {
            SetActiveTab(tabName);
            vm.HitsFilter = filter;
        }

        private void SetActiveTab(string activeTab)
        {
            ResetTabStyles();
            SetTabStyle(activeTab);
        }

        private void ResetTabStyles()
        {
            var inactiveColor = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
            var inactiveForeground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

            HitsTabButton.Background = inactiveColor;
            HitsTabButton.Foreground = inactiveForeground;
            CustomTabButton.Background = inactiveColor;
            CustomTabButton.Foreground = inactiveForeground;
            ToCheckTabButton.Background = inactiveColor;
            ToCheckTabButton.Foreground = inactiveForeground;
        }

        private void SetTabStyle(string activeTab)
        {
            var tabStyles = new Dictionary<string, (Color color, System.Windows.Media.Brush foreground)>
            {
                ["Hits"] = (Color.FromRgb(0x28, 0xA7, 0x45), System.Windows.Media.Brushes.White),
                ["Custom"] = (Color.FromRgb(0x6F, 0x42, 0xC1), System.Windows.Media.Brushes.White),
                ["ToCheck"] = (Color.FromRgb(0xFF, 0xC1, 0x07), System.Windows.Media.Brushes.White)
            };

            if (tabStyles.TryGetValue(activeTab, out var style))
            {
                var button = activeTab switch
                {
                    "Hits" => HitsTabButton,
                    "Custom" => CustomTabButton,
                    "ToCheck" => ToCheckTabButton,
                    _ => null
                };

                if (button != null)
                {
                    button.Background = new SolidColorBrush(style.color);
                    button.Foreground = style.foreground;
                }
            }
        }

        private void CopySelectedBotData(object sender, RoutedEventArgs e) => GetSelectedBots().CopyToClipboard(b => b.Data);
        private void CopySelectedBotProxy(object sender, RoutedEventArgs e) => GetSelectedBots().CopyToClipboard(b => b.Proxy);
        private void CopySelectedBotInfo(object sender, RoutedEventArgs e) => GetSelectedBots().CopyToClipboard(b => b.Info ?? "");
        private void SelectAllBots(object sender, RoutedEventArgs e) => botsListView.SelectAll();

        private async Task ExecuteSafely(Func<Task> action)
        {
            try { await action(); } catch (Exception ex) { Alert.Exception(ex); }
        }

        private void ExecuteSafely(Action action)
        {
            try { action(); } catch (Exception ex) { Alert.Exception(ex); }
        }

    }
}