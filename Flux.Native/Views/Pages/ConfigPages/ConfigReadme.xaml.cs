using Flux.Native.Extensions;
using Flux.Native.ViewModels.Configs;
using System.Windows.Controls;

using Flux.Native.Services;

namespace Flux.Native.Views.Pages.Configs
{
    /// <summary>
    /// Interaction logic for ConfigReadme.xaml
    /// </summary>
    public partial class ConfigReadme : Page, IUpdatablePage
    {
        private readonly ConfigReadmeViewModel vm;

        public ConfigReadme(ConfigReadmeViewModel vm)
        {
            this.vm = vm;
            DataContext = this.vm;

            InitializeComponent();
        }

        // TODO: Find out why the preview doesn't update when navigating to the page
        public void UpdateViewModel()
        {
            vm.UpdateViewModel();
            readmeRTB.Document.Blocks.Clear();
            readmeRTB.AppendText(vm.Readme);
        }

        private void ReadmeChanged(object sender, TextChangedEventArgs e)
        {
            var newText = readmeRTB.GetText();

            if (!string.IsNullOrWhiteSpace(newText))
            {
                vm.Readme = newText;
            }
        }
    }
}






