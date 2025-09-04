using OpenBullet2.Core.Models.Jobs;
using OpenBullet2.Native.Views.Pages;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenBullet2.Native.Views.Dialogs
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

        private void Cancel(object sender, RoutedEventArgs e) => ((MainDialog)Parent).Close();

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
                    new MainDialog(new MultiRunJobOptionsDialog(null, onAccept), "Create Multi-Run Job", 1100, 800).ShowDialog();
                    break;

                case JobType.ProxyCheck:
                    new MainDialog(new ProxyCheckJobOptionsDialog(null, onAccept), "Create Proxy Check Job", 900, 650).ShowDialog();
                    break;
            }

            ((MainDialog)Parent).Close();
        }
    }
}
