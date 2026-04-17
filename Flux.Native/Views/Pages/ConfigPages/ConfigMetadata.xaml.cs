using Microsoft.Win32;
using Flux.Native.Helpers;
using Flux.Native.ViewModels.Configs;
using System;
using System.Windows;
using System.Windows.Controls;

using Flux.Native.Services;

namespace Flux.Native.Views.Pages.Configs
{
    /// <summary>
    /// Interaction logic for ConfigMetadata.xaml
    /// </summary>
    public partial class ConfigMetadata : Page, IUpdatablePage
    {
        private readonly ConfigMetadataViewModel vm;

        public ConfigMetadata(ConfigMetadataViewModel vm)
        {
            this.vm = vm;
            DataContext = this.vm;

            InitializeComponent();
        }

        public void UpdateViewModel() => vm.UpdateViewModel();

        private void OpenIcon(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Images | *.ico;*.jpg;*.jpeg;*.png;*.bmp",
                FilterIndex = 1
            };

            ofd.ShowDialog();
            
            if (!string.IsNullOrEmpty(ofd.FileName))
            {
                try
                {
                    vm.SetIconFromFile(ofd.FileName);
                }
                catch (Exception ex)
                {
                    Alert.Exception(ex);
                }
            }
        }

        private async void DownloadIcon(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.SetIconFromUrlAsync(urlTextbox.Text);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }
    }
}






