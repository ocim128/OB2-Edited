using Flux.Core.Models.Jobs;
using Flux.Native.Helpers;
using Flux.Native.Services;
using Flux.Native.ViewModels;
using Flux.Native.ViewModels.Base;
using Flux.Native.ViewModels.Jobs;
using Flux.Native.Views.Dialogs.Job;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;


namespace Flux.Native.Views.Pages.Jobs;

/// <summary>
/// Interaction logic for Jobs.xaml - Uses centralized service retrieval
/// </summary>
public partial class Jobs : Page
{
        private readonly MainWindow mainWindow;
        private readonly JobsViewModel vm;

        public Jobs()
        {
            var helper = new PageHelper(this);
            
            // Use centralized service retrieval with proper error handling
            mainWindow = helper.GetRequiredService<MainWindow>();
            var viewModelsService = helper.GetRequiredService<ViewModelsService>();
            vm = viewModelsService.Jobs ?? throw new InvalidOperationException("Jobs ViewModel is null");

            DataContext = vm;
            InitializeComponent();
        }

        private void NewJob(object sender, RoutedEventArgs e)
            => Alert.ShowDialog(new CreateJobDialog(this), "Select job type");

        private async void RemoveAll(object sender, RoutedEventArgs e)
        {
            await Alert.SafeExecuteAsync(() => vm.RemoveAllAsync(), "removing all jobs");
        }

        private void EditJob(object sender, RoutedEventArgs e) => EditJob(UIHelpers.GetButtonTag<JobViewModel>(sender));

        public async void EditJob(JobViewModel jobVM)
    {
        try
        {
            var snapshot = await vm.GetJobOptionsAsync(jobVM.Id);
            if (snapshot is null)
            {
                throw new InvalidOperationException($"Job {jobVM.Id} could not be loaded");
            }

            var jobOptions = snapshot.Options;
            Action<JobOptions> onAccept = async options =>
            {
                jobVM = await vm.EditJobAsync(jobVM.Id, options);
                mainWindow.DisplayJob(jobVM);
            };

            if (snapshot.JobType is JobType.MultiRun)
            {
                var page = new MultiRunJobOptionsDialog(jobOptions as MultiRunJobOptions, onAccept);
                Alert.ShowDialog(page, $"Edit job #{snapshot.JobId}", 1100, 800);
            }
            else if (snapshot.JobType is JobType.ProxyCheck)
            {
                var page = new ProxyCheckJobOptionsDialog(jobOptions as ProxyCheckJobOptions, onAccept);
                Alert.ShowDialog(page, $"Edit job #{snapshot.JobId}", 800, 600);
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
            var snapshot = await vm.GetJobOptionsAsync(jobVM.Id, clone: true);
            if (snapshot is null)
            {
                throw new InvalidOperationException($"Job {jobVM.Id} could not be loaded");
            }

            Action<JobOptions> onAccept = async options =>
            {
                var cloned = await vm.CreateJobAsync(options);
                mainWindow.DisplayJob(cloned);
            };

            if (snapshot.JobType is JobType.MultiRun)
            {
                var page = new MultiRunJobOptionsDialog(snapshot.Options as MultiRunJobOptions, onAccept);
                Alert.ShowDialog(page, $"Clone job #{snapshot.JobId}", 1100, 800);
            }
            else if (snapshot.JobType is JobType.ProxyCheck)
            {
                var page = new ProxyCheckJobOptionsDialog(snapshot.Options as ProxyCheckJobOptions, onAccept);
                Alert.ShowDialog(page, $"Clone job #{snapshot.JobId}", 800, 600);
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



