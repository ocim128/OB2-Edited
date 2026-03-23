using System.Windows.Controls;
using System.Windows.Input;
using Flux.Native.ViewModels.Tools;

namespace Flux.Native.Views.Controls.Tools;

public partial class BookmarkletToolControl : UserControl
{
    public BookmarkletToolControl()
    {
        InitializeComponent();
    }

    private void BookmarkletInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            DataContext is BookmarkletToolViewModel viewModel)
        {
            viewModel.Parse();
            e.Handled = true;
        }
    }
}
