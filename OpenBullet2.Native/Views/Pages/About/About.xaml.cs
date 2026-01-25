using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Views.Dialogs.Common;
using System.Windows;
using System.Windows.Controls;

namespace OpenBullet2.Native.Views.Pages.About
{
    /// <summary>
    /// Interaction logic for About.xaml
    /// </summary>
    public partial class About : Page
    {
        public About()
        {
            InitializeComponent();
        }

        private void OpenLicense(object sender, RoutedEventArgs e) => new MainDialog(new LicenseDialog(), "License", true).ShowDialog();

        private void OpenDonation(object sender, RoutedEventArgs e) => Url.Open(AppConstants.DonationUrl);

        private void OpenRepository(object sender, RoutedEventArgs e) => Url.Open(AppConstants.RepositoryUrl);

        private void OpenForum(object sender, RoutedEventArgs e) => Url.Open(AppConstants.ForumUrl);

        private void OpenIssues(object sender, RoutedEventArgs e) => Url.Open(AppConstants.IssuesUrl);
    }
}


