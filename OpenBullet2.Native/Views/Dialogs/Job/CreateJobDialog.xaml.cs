using OpenBullet2.Core.Models.Jobs;
using OpenBullet2.Native.Views.Pages;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.ViewModels.Base;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenBullet2.Native.Views.Dialogs.Job
{
    /// <summary>
    /// Interaction logic for CreateJobDialog.xaml
    /// </summary>
    public partial class CreateJobDialog : Page
    {
        private readonly object caller;

        public CreateJobDialog(object caller)
        {
            this.caller = caller;

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
                    Alert.ShowDialog(new MultiRunJobOptionsDialog(null, onAccept), "Create Multi-Run Job", 1100, 800);
                    break;

                case JobType.ProxyCheck:
                    Alert.ShowDialog(new ProxyCheckJobOptionsDialog(null, onAccept), "Create Proxy Check Job", 900, 650);
                    break;
            }

            UIHelpers.CloseParentDialog(this);
        }
    }
}
