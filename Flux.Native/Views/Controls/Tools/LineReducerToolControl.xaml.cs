using Microsoft.Win32;
using System.Windows.Controls;
using Flux.Native.ViewModels.Tools;

namespace Flux.Native.Views.Controls.Tools;

public partial class LineReducerToolControl : UserControl
{
    public LineReducerToolControl()
    {
        InitializeComponent();
    }

    private void BrowseLineReducerSource(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not LineReducerToolViewModel viewModel || viewModel.IsBusy)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Select main text file"
        };

        if (dialog.ShowDialog() == true)
        {
            viewModel.SetSourcePath(dialog.FileName);
        }
    }

    private void BrowseLineReducerOutput(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not LineReducerToolViewModel viewModel || viewModel.IsBusy)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Choose output file",
            FileName = !string.IsNullOrWhiteSpace(viewModel.OutputPath)
                ? viewModel.OutputPath
                : viewModel.SourcePath
        };

        if (dialog.ShowDialog() == true)
        {
            viewModel.SetOutputPath(dialog.FileName);
        }
    }

    private void AddLineReducerCompareFiles(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not LineReducerToolViewModel viewModel || viewModel.IsBusy)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true,
            Title = "Add comparison files"
        };

        if (dialog.ShowDialog() == true)
        {
            viewModel.AddCompareFiles(dialog.FileNames);
        }
    }
}
