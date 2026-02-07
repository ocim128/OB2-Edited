using Flux.Native.Extensions;
using Flux.Native.Services;
using Flux.Native.ViewModels;
using Flux.Native.ViewModels.Configs;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;


namespace Flux.Native.Views.Pages.Configs
{
    /// <summary>
    /// Interaction logic for ConfigReadme.xaml
    /// </summary>
    public partial class ConfigReadme : Page
    {
        private readonly ConfigReadmeViewModel vm;

        public ConfigReadme()
        {
            vm = App.ServiceProvider.GetRequiredService<ViewModelsService>().ConfigReadme;
            DataContext = vm;

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






