using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Flux.Native.ViewModels.Configs;

namespace Flux.Native.Views.Controls.Config;

public partial class ConfigStackerBlockListControl : UserControl
{
    public event Action? AddBlockRequested;

    public ConfigStackerBlockListControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfigStackerViewModel viewModel)
        {
            viewModel.SelectionBringIntoViewRequested += BringBlockIntoView;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfigStackerViewModel viewModel)
        {
            viewModel.SelectionBringIntoViewRequested -= BringBlockIntoView;
        }
    }

    private void AddBlock(object sender, RoutedEventArgs e)
    {
        AddBlockRequested?.Invoke();
    }

    private void SelectBlock(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BlockViewModel block } || DataContext is not ConfigStackerViewModel viewModel)
        {
            return;
        }

        var ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        var shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        viewModel.HandleBlockClick(block, ctrl, shift);
    }

    private void BringBlockIntoView(BlockViewModel block)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                BlocksListBox.UpdateLayout();
                BlocksListBox.ScrollIntoView(block);

                var container = BlocksListBox.ItemContainerGenerator.ContainerFromItem(block) as FrameworkElement;
                container?.BringIntoView();
            }
            catch
            {
            }
        }), DispatcherPriority.ContextIdle);
    }
}
