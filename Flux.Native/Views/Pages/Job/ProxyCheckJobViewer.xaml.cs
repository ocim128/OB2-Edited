using Flux.Core.Services;
using Flux.Native.Helpers;
using Flux.Native.ViewModels.Jobs;
using Flux.Native.Views.Dialogs.Job;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;


namespace Flux.Native.Views.Pages.Jobs;

/// <summary>
/// Interaction logic for ProxyCheckJobViewer.xaml
/// </summary>
public partial class ProxyCheckJobViewer : Page, IDisposable
{
        private readonly FluxSettingsService fluxSettingsService;
        private readonly JobManagerService jobManager;
        private ProxyCheckJobViewerViewModel vm;
        private volatile bool disposed;

        public ProxyCheckJobViewer(FluxSettingsService fluxSettingsService, JobManagerService jobManager)
        {
            this.fluxSettingsService = fluxSettingsService;
            this.jobManager = jobManager;
            InitializeComponent();
        }

        public void BindViewModel(ProxyCheckJobViewModel jobVM)
        {
            if (disposed)
            {
                return;
            }

            CleanupViewModel();

            vm = new ProxyCheckJobViewerViewModel(jobVM, jobManager);
            vm.NewMessage += OnResultMessage;
            DataContext = vm;
        }

        private async void Start(object sender, RoutedEventArgs e)
        {
            await Alert.SafeExecuteAsync(async () =>
            {
                jobLog.Clear();
                jobLog.BufferSize = fluxSettingsService.Settings.GeneralSettings.LogBufferSize;
                await vm.Start();
            }, "starting proxy check job");
        }

        private async void Stop(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.Stop(), "stopping proxy check job");

        private async void Pause(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.Pause(), "pausing proxy check job");

        private async void Resume(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.Resume(), "resuming proxy check job");

        private async void Abort(object sender, RoutedEventArgs e)
            => await Alert.SafeExecuteAsync(() => vm.Abort(), "aborting proxy check job");

        private void SkipWait(object sender, RoutedEventArgs e)
            => Alert.SafeExecute(() => vm.SkipWait(), "skipping wait");

        private void ChangeBots(object sender, MouseButtonEventArgs e)
            => new MainDialog(new ChangeBotsDialog(this, vm.Job.Bots), "Change bots").ShowDialog();

        public async void ChangeBots(int newValue)
            => await Alert.SafeExecuteAsync(() => vm.ChangeBotsAsync(newValue), "changing bot count");

        private void OnResultMessage(object sender, string message, Color color)
        {
            if (disposed)
            {
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!disposed && fluxSettingsService.Settings.GeneralSettings.EnableJobLogging)
                {
                    jobLog.Append(message, color);
                }
            });
        }

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
            vm.Dispose();
            vm = null;
            DataContext = null;
        }
    }
