using Flux.Native.Services;
using Flux.Native.ViewModels;
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

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:84.0) Gecko/20100101 Firefox/84.0");

                try
                {
                    var response = await client.GetAsync($"https://raw.githubusercontent.com/openbullet/Flux/master/Changelog/{currentVersion}.md");
                    Text = await response.Content.ReadAsStringAsync();
                }
                catch
                {
                    Text = "Could not retrieve the changelog";
                }
            }
        }
    }
}
