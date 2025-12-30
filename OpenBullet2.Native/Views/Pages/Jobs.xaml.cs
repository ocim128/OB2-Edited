using Newtonsoft.Json;
using OpenBullet2.Core.Models.Jobs;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.ViewModels.Infrastructure;
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
    /// Interaction logic for Jobs.xaml - Uses centralized service retrieval
    /// </summary>
    public partial class Jobs : Page
    {
        private readonly MainWindow mainWindow;
        private readonly IJobRepository jobRepo;
        private readonly JobsViewModel vm;
        private readonly PageHelper helper;

        public Jobs()
        {
            helper = new PageHelper(this);
            
            // Use centralized service retrieval with proper error handling
            mainWindow = helper.GetRequiredService<MainWindow>();
            jobRepo = helper.GetRequiredService<IJobRepository>();
            var viewModelsService = helper.GetRequiredService<ViewModelsService>();
            vm = viewModelsService.Jobs ?? throw new InvalidOperationException("Jobs ViewModel is null");

            DataContext = vm;
            InitializeComponent();
        }

        private void NewJob(object sender, RoutedEventArgs e)
            => Alert.ShowDialog(new CreateJobDialog(this), "Select job type");

        private void RemoveAll(object sender, RoutedEventArgs e)
        {
            // Use centralized exception handling
            UIHelpers.HandleUIException(() => vm.RemoveAll(), "removing all jobs");
        }

        private void EditJob(object sender, RoutedEventArgs e) => EditJob(UIHelpers.GetButtonTag<JobViewModel>(sender));

        public async void EditJob(JobViewModel jobVM)
    {
        try
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
                Alert.ShowDialog(page, $"Edit job #{entity.Id}", 1100, 800);
            }
            else if (jobVM is ProxyCheckJobViewModel)
            {
                var page = new ProxyCheckJobOptionsDialog(jobOptions as ProxyCheckJobOptions, onAccept);
                Alert.ShowDialog(page, $"Edit job #{entity.Id}", 800, 600);
            }
            else
            {
                throw new NotImplementedException();
            }
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }

        public async void CloneJob(object sender, RoutedEventArgs e)
    {
        try
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
                Alert.ShowDialog(page, $"Clone job #{entity.Id}", 1100, 800);
            }
            else if (jobVM is ProxyCheckJobViewModel)
            {
                var page = new ProxyCheckJobOptionsDialog(newOptions as ProxyCheckJobOptions, onAccept);
                Alert.ShowDialog(page, $"Clone job #{entity.Id}", 800, 600);
            }
            else
            {
                throw new NotImplementedException();
            }
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }

        private async void RemoveJob(object sender, RoutedEventArgs e)
        {
            // Use centralized exception handling
            await Alert.SafeExecuteAsync(async () =>
            {
                await vm.RemoveJobAsync(UIHelpers.GetButtonTag<JobViewModel>(sender));
            }, "removing job");
        }

        public async void CreateJob(JobOptions options)
    {
        try
        {
            await vm.CreateJobAsync(options);
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }

        private void ViewJob(object sender, MouseButtonEventArgs e)
        {
            // Use centralized exception handling and tag extraction
            UIHelpers.HandleUIException(() =>
            {
                var jobVM = UIHelpers.GetButtonTag<JobViewModel>(sender);
                if (jobVM != null)
                {
                    mainWindow.DisplayJob(jobVM);
                }
            }, "viewing job");
        }
    }
}
