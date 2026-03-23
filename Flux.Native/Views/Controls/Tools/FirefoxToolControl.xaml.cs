using Microsoft.Win32;
using System.Windows.Controls;
using Flux.Native.ViewModels.Tools;

namespace Flux.Native.Views.Controls.Tools;

public partial class FirefoxToolControl : UserControl
{
    public FirefoxToolControl()
    {
        InitializeComponent();
    }

    private void SelectZipForOptions(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not FirefoxToolViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "ZIP archives (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Select a ZIP archive"
        };

        if (dialog.ShowDialog() == true)
        {
            viewModel.LoadArchive(dialog.FileName);
        }
    }
}
