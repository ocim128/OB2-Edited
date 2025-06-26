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
            try
            {
                System.Diagnostics.Debug.WriteLine("Jobs constructor started");
                
                // Initialize services with null checks
            mainWindow = SP.GetService<MainWindow>();
                if (mainWindow == null)
                    throw new InvalidOperationException("MainWindow service is null");
                System.Diagnostics.Debug.WriteLine("MainWindow service retrieved");
                
            jobRepo = SP.GetService<IJobRepository>();
                if (jobRepo == null)
                    throw new InvalidOperationException("JobRepository service is null");
                System.Diagnostics.Debug.WriteLine("JobRepository service retrieved");
                
                var viewModelsService = SP.GetService<ViewModelsService>();
                if (viewModelsService == null)
                    throw new InvalidOperationException("ViewModelsService is null");
                System.Diagnostics.Debug.WriteLine("ViewModelsService retrieved");
                
                vm = viewModelsService.Jobs;
                if (vm == null)
                    throw new InvalidOperationException("Jobs ViewModel is null");
                System.Diagnostics.Debug.WriteLine("JobsViewModel retrieved");
                
                // Try to set DataContext before InitializeComponent
            DataContext = vm;
                System.Diagnostics.Debug.WriteLine("DataContext set");

                // Initialize component with detailed logging
                System.Diagnostics.Debug.WriteLine("About to call InitializeComponent");
            InitializeComponent();
                System.Diagnostics.Debug.WriteLine("InitializeComponent completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Jobs constructor error: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                try
                {
                    MessageBox.Show($"Failed to initialize Jobs page: {ex.Message}\n\nType: {ex.GetType().Name}", 
                                  "Jobs Page Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch
                {
                    // MessageBox might fail too, so fallback to console
                    Console.WriteLine($"CRITICAL: Jobs constructor failed: {ex}");
                }
                
                throw;
            }
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
                var dialog = new MultiRunJobOptionsDialog(jobOptions as MultiRunJobOptions, onAccept);
                dialog.ShowDialog();
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

        private async void CloneJob(object sender, RoutedEventArgs e)
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
                var dialog = new MultiRunJobOptionsDialog(newOptions as MultiRunJobOptions, onAccept);
                dialog.ShowDialog();
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
                System.Diagnostics.Debug.WriteLine($"ViewJob called with sender: {sender?.GetType().Name}");
                
                if (sender is Grid grid)
                {
                    System.Diagnostics.Debug.WriteLine($"Grid found, Tag type: {grid.Tag?.GetType().Name}");
                    if (grid.Tag is JobViewModel jobVM)
                    {
                        System.Diagnostics.Debug.WriteLine($"JobViewModel found: {jobVM.Id}");
                        var mainWindow = SP.GetService<MainWindow>();
                        System.Diagnostics.Debug.WriteLine("MainWindow service retrieved");
                        mainWindow.DisplayJob(jobVM);
                        System.Diagnostics.Debug.WriteLine("DisplayJob completed");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Grid.Tag is not a JobViewModel");
                    }
                }
                else if (sender is WrapPanel wrapPanel)
                {
                    System.Diagnostics.Debug.WriteLine($"WrapPanel found, Tag type: {wrapPanel.Tag?.GetType().Name}");
                    if (wrapPanel.Tag is JobViewModel jobVM)
                    {
                        System.Diagnostics.Debug.WriteLine($"JobViewModel found: {jobVM.Id}");
                        var mainWindow = SP.GetService<MainWindow>();
                        System.Diagnostics.Debug.WriteLine("MainWindow service retrieved");
                        mainWindow.DisplayJob(jobVM);
                        System.Diagnostics.Debug.WriteLine("DisplayJob completed");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Unexpected sender type: {sender?.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ViewJob Exception: {ex}");
                Alert.Exception(ex);
            }
        }
    }
}
