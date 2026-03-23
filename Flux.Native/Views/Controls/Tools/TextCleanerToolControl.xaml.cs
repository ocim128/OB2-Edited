using System.Windows.Controls;
using System.Windows.Input;
using Flux.Native.ViewModels.Tools;

namespace Flux.Native.Views.Controls.Tools;

public partial class TextCleanerToolControl : UserControl
{
    public TextCleanerToolControl()
    {
        InitializeComponent();
    }

    private void TextCleanerInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            DataContext is TextCleanerToolViewModel viewModel)
        {
            viewModel.Clean();
            e.Handled = true;
        }
    }
}
