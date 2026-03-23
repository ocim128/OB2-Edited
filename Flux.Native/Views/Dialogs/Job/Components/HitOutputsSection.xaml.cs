using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Flux.Core.Models.Hits;

namespace Flux.Native.Views.Dialogs.Job.Components
{
    public partial class HitOutputsSection : UserControl
    {
        public HitOutputsSection()
        {
            InitializeComponent();
        }

        private void AddDatabaseHitOutput(object sender, RoutedEventArgs e)
        {
            if (DataContext is MultiRunJobOptionsViewModel vm)
            {
                vm.AddDatabaseHitOutput();
            }
        }

        private void AddFileSystemHitOutput(object sender, RoutedEventArgs e)
        {
            if (DataContext is MultiRunJobOptionsViewModel vm)
            {
                vm.AddFileSystemHitOutput();
            }
        }

        private void AddDiscordWebhookHitOutput(object sender, RoutedEventArgs e)
        {
            if (DataContext is MultiRunJobOptionsViewModel vm)
            {
                vm.AddDiscordWebhookHitOutput();
            }
        }

        private void AddTelegramBotHitOutput(object sender, RoutedEventArgs e)
        {
            if (DataContext is MultiRunJobOptionsViewModel vm)
            {
                vm.AddTelegramBotHitOutput();
            }
        }

        private void AddCustomWebhookHitOutput(object sender, RoutedEventArgs e)
        {
            if (DataContext is MultiRunJobOptionsViewModel vm)
            {
                vm.AddCustomWebhookHitOutput();
            }
        }

        private void RemoveHitOutput(object sender, RoutedEventArgs e)
        {
             if (DataContext is MultiRunJobOptionsViewModel vm && (sender as Button)?.Tag is HitOutputOptions option)
            {
                vm.RemoveHitOutput(option);
            }
        }

        private void ChildScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled) return;

            if (sender is not ScrollViewer child) return;

            // Determine if child can scroll in the direction of the wheel
            bool scrollingUp = e.Delta > 0;
            bool atTop = child.VerticalOffset <= 0;
            bool atBottom = child.VerticalOffset >= child.ScrollableHeight;

            bool shouldBubble = (scrollingUp && atTop) || (!scrollingUp && atBottom) || child.ScrollableHeight == 0;

            if (!shouldBubble)
            {
                // Let the child scroll
                return;
            }

            // Bubble to parent ScrollViewer (main page)
            var parentScroll = ConfigSelectionSection.FindParent<ScrollViewer>(this); // We want the parent scrollviewer
            
            if (parentScroll is null) return;

            e.Handled = true; // prevent the child from handling it
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            parentScroll.RaiseEvent(eventArg);
        }
    }
}
