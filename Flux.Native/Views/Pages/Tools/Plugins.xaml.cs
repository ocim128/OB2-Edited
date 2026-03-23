using Microsoft.Win32;
using Flux.Native.ViewModels.Tools;
using System.Windows;
using System.Windows.Controls;


namespace Flux.Native.Views.Pages.Tools
{
    /// <summary>
    /// Interaction logic for Plugins.xaml
    /// </summary>
    public partial class Plugins : Page
    {
        private readonly PluginsViewModel vm;

        public Plugins(PluginsViewModel vm)
        {
            this.vm = vm;
            DataContext = this.vm;

            InitializeComponent();
        }

        public void Refresh()
        {
            vm.RefreshList();
        }

        private void AddPlugin(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Plugin Files(*.zip)|*.zip",
                FilterIndex = 1
            };

            ofd.ShowDialog();

            if (!string.IsNullOrWhiteSpace(ofd.FileName))
            {
                vm.Add(ofd.FileName);
            }
        }

        private void RemovePlugin(object sender, RoutedEventArgs e) => vm.Delete((PluginInfo)(sender as Button).Tag);
    }
}


