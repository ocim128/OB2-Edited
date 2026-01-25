using OpenBullet2.Core.Services;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.ViewModels.Jobs;
using OpenBullet2.Native.Views.Dialogs.Job;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;


namespace OpenBullet2.Native.Views.Pages.Jobs;

/// <summary>
/// Interaction logic for ProxyCheckJobViewer.xaml
/// </summary>
public partial class ProxyCheckJobViewer : Page
{
        private readonly OpenBulletSettingsService obSettingsService;
        private ProxyCheckJobViewerViewModel vm;

        public ProxyCheckJobViewer()
        {
            obSettingsService = App.ServiceProvider.GetRequiredService<OpenBulletSettingsService>();
            InitializeComponent();
        }

        public void BindViewModel(ProxyCheckJobViewModel jobVM)
        {
            if (vm is not null)
            {
                vm.Dispose();

                try
                {
                    vm.NewMessage -= OnResultMessage;
                }
                catch
                {

                }
            }

            vm = new ProxyCheckJobViewerViewModel(jobVM);
            vm.NewMessage += OnResultMessage;
            DataContext = vm;
        }

        private async void Start(object sender, RoutedEventArgs e)
        {
            await Alert.SafeExecuteAsync(async () =>
            {
                Application.Current.Dispatcher.Invoke(() => jobLog.Clear());
                jobLog.BufferSize = obSettingsService.Settings.GeneralSettings.LogBufferSize;
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
            => Application.Current.Dispatcher.Invoke(() =>
            {
                if (obSettingsService.Settings.GeneralSettings.EnableJobLogging)
                {
                    jobLog.Append(message, color);
                }
            });
    }



