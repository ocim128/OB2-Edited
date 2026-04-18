using Flux.Native.Services;
using Flux.Native.ViewModels;
using System;
using System.Net.Http;
using System.Windows.Controls;
using Flux.Native.ViewModels.Base;

namespace Flux.Native.Views.Dialogs.Updates
{
    /// <summary>
    /// Interaction logic for ShowChangelogDialog.xaml
    /// </summary>
    public partial class ShowChangelogDialog : Page
    {
        private ChangelogViewModel vm;

        public ShowChangelogDialog()
        {
            InitializeComponent();
            vm = new ChangelogViewModel();
            DataContext = vm;
        }

        public class ChangelogViewModel : ViewModelBase
        {
            private static readonly Lazy<HttpClient> SharedClient = new(() =>
            {
                var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Flux-Native/1.0");
                return client;
            });

            private string text = "Loading...";
            public string Text
            {
                get => text;
                set
                {
                    text = value;
                    OnPropertyChanged();
                }
            }

            public ChangelogViewModel()
            {
                FetchChangelog();
            }

            private async void FetchChangelog()
            {
                // Get current version from assembly
                var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.2.2";

                try
                {
                    using var response = await SharedClient.Value.GetAsync($"https://raw.githubusercontent.com/openbullet/Flux/master/Changelog/{currentVersion}.md").ConfigureAwait(false);
                    Text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch
                {
                    Text = "Could not retrieve the changelog";
                }
            }
        }
    }
}
