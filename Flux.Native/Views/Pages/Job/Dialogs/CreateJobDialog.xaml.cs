using Flux.Core.Models.Jobs;
using Flux.Native.Factories;
using Flux.Native.Views.Pages;
using Flux.Native.Views.Pages.Jobs;
using Flux.Native.Helpers;
using Flux.Native.ViewModels.Base;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Flux.Native.Views.Dialogs.Job
{
    /// <summary>
    /// Interaction logic for CreateJobDialog.xaml
    /// </summary>
    public partial class CreateJobDialog : Page
    {
        private readonly object caller;
        private readonly IJobOptionsDialogFactory jobOptionsDialogFactory;

        public CreateJobDialog(object caller, IJobOptionsDialogFactory jobOptionsDialogFactory)
        {
            this.caller = caller;
            this.jobOptionsDialogFactory = jobOptionsDialogFactory;

            InitializeComponent();
        }

        private void CreateMultiRunJob(object sender, MouseButtonEventArgs e) => CreateJob(JobType.MultiRun);
        private void CreateProxyCheckJob(object sender, MouseButtonEventArgs e) => CreateJob(JobType.ProxyCheck);

        private void Cancel(object sender, RoutedEventArgs e) => UIHelpers.CloseParentDialog(this);

        private void CreateJob(JobType type)
        {
            Action<JobOptions> onAccept = options =>
            {
                if (caller is Jobs page)
                {
                    page.CreateJob(options);
                }
            };

            switch (type)
            {
                case JobType.MultiRun:
                    Alert.ShowDialog(jobOptionsDialogFactory.CreateMultiRun(onAccept: onAccept), "Create Multi-Run Job", 1100, 800);
                    break;

                case JobType.ProxyCheck:
                    Alert.ShowDialog(jobOptionsDialogFactory.CreateProxyCheck(onAccept: onAccept), "Create Proxy Check Job", 900, 650);
                    break;
            }

            UIHelpers.CloseParentDialog(this);
        }
    }
}
