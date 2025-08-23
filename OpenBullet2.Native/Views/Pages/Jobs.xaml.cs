using Newtonsoft.Json;
using OpenBullet2.Core.Models.Jobs;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.Views.Dialogs;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;
using OpenBullet2.Native.Infrastructure.DependencyInjection;

namespace OpenBullet2.Native.Views.Pages
{
    /// <summary>
    /// Interaction logic for Jobs.xaml
    /// </summary>
    public partial class Jobs : Page
    {
        private readonly MainWindow mainWindow;
        private readonly IJobRepository jobRepo;
        private readonly JobsViewModel vm;

        public Jobs()
        {
            mainWindow = ServiceLocator.GetService<MainWindow>() ?? throw new InvalidOperationException("MainWindow service is null");
            jobRepo = ServiceLocator.GetService<IJobRepository>() ?? throw new InvalidOperationException("JobRepository service is null");
            var viewModelsService = ServiceLocator.GetService<ViewModelsService>() ?? throw new InvalidOperationException("ViewModelsService is null");
            vm = viewModelsService.Jobs ?? throw new InvalidOperationException("Jobs ViewModel is null");

            DataContext = vm;
            InitializeComponent();
        }

        private void NewJob(object sender, RoutedEventArgs e)
            => new MainDialog(new CreateJobDialog(this), "Select job type").ShowDialog();

        private void RemoveAll(object sender, RoutedEventArgs e)
        {
            try
            {
                vm.RemoveAll();
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void EditJob(object sender, RoutedEventArgs e) => EditJob((JobViewModel)(sender as Button).Tag);

        public async void EditJob(JobViewModel jobVM)
        {
            var entity = await jobRepo.GetAsync(jobVM.Id);
            var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var jobOptions = JsonConvert.DeserializeObject<JobOptionsWrapper>(entity.JobOptions, jsonSettings).Options;
            Action<JobOptions> onAccept = async options =>
            {
                jobVM = await vm.EditJobAsync(entity, options);
                mainWindow.DisplayJob(jobVM);
            };

            if (jobVM is MultiRunJobViewModel)
            {
                var page = new MultiRunJobOptionsDialog(jobOptions as MultiRunJobOptions, onAccept);
                new MainDialog(page, $"Edit job #{entity.Id}", 1100, 800).ShowDialog();
            }
            else if (jobVM is ProxyCheckJobViewModel)
            {
                var page = new ProxyCheckJobOptionsDialog(jobOptions as ProxyCheckJobOptions, onAccept);
                new MainDialog(page, $"Edit job #{entity.Id}", 800, 600).ShowDialog();
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public async void CloneJob(object sender, RoutedEventArgs e)
        {
            var jobVM = (JobViewModel)(sender as Button).Tag;
            var entity = await jobRepo.GetAsync(jobVM.Id);
            var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var oldOptions = JsonConvert.DeserializeObject<JobOptionsWrapper>(entity.JobOptions, jsonSettings).Options;
            var newOptions = JobOptionsFactory.CloneExistant(oldOptions);

            Action<JobOptions> onAccept = async options =>
            {
                var cloned = await vm.CloneJobAsync(entity.JobType, options);
                mainWindow.DisplayJob(cloned);
            };

            if (jobVM is MultiRunJobViewModel)
            {
                var page = new MultiRunJobOptionsDialog(newOptions as MultiRunJobOptions, onAccept);
                new MainDialog(page, $"Clone job #{entity.Id}", 1100, 800).ShowDialog();
            }
            else if (jobVM is ProxyCheckJobViewModel)
            {
                var page = new ProxyCheckJobOptionsDialog(newOptions as ProxyCheckJobOptions, onAccept);
                new MainDialog(page, $"Clone job #{entity.Id}", 800, 600).ShowDialog();
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private async void RemoveJob(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.RemoveJobAsync((JobViewModel)(sender as Button).Tag);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        public async void CreateJob(JobOptions options) => await vm.CreateJobAsync(options);

        private void ViewJob(object sender, MouseButtonEventArgs e)
        {
            try
            {
                JobViewModel jobVM = null;

                if (sender is Grid grid && grid.Tag is JobViewModel gridJobVM)
                {
                    jobVM = gridJobVM;
                }
                else if (sender is WrapPanel wrapPanel && wrapPanel.Tag is JobViewModel panelJobVM)
                {
                    jobVM = panelJobVM;
                }

                if (jobVM != null)
                {
                    mainWindow.DisplayJob(jobVM);
                }
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }
    }
}
