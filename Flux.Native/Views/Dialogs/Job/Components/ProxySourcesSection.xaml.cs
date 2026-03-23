using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Flux.Native.ViewModels.Jobs;
using Microsoft.Win32;

namespace Flux.Native.Views.Dialogs.Job.Components
{
    public partial class ProxySourcesSection : UserControl
    {
        public ProxySourcesSection()
        {
            InitializeComponent();
        }

        private void AddGroupProxySource(object sender, RoutedEventArgs e)
        {
            if (DataContext is MultiRunJobOptionsViewModel vm)
            {
                vm.AddGroupProxySource();
            }
        }

        private void AddFileProxySource(object sender, RoutedEventArgs e)
        {
            if (DataContext is MultiRunJobOptionsViewModel vm)
            {
                vm.AddFileProxySource();
            }
        }

        private void AddRemoteProxySource(object sender, RoutedEventArgs e)
        {
            if (DataContext is MultiRunJobOptionsViewModel vm)
            {
                vm.AddRemoteProxySource();
            }
        }

        private void RemoveProxySource(object sender, RoutedEventArgs e)
        {
            if (DataContext is MultiRunJobOptionsViewModel vm && (sender as Button)?.Tag is ProxySourceOptionsViewModel option)
            {
                vm.RemoveProxySource(option);
            }
        }

        private void SelectFileForProxySource(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Proxy files or Shell scripts echoing proxies one by one | *.txt;*.bat;*.ps1;*.sh",
                FilterIndex = 1
            };

            if (ofd.ShowDialog() == true)
            {
                if ((sender as Button)?.Tag is FileProxySourceOptionsViewModel vm)
                {
                    vm.FileName = ofd.FileName;
                }
            }
        }

        // Forward mouse wheel from child scroll viewers
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
            var parentScroll = ConfigSelectionSection.FindParent<ScrollViewer>(this); // We want the parent scrollviewer, not the page strictly.
            // Actually, finding any ancestor ScrollViewer is better.
            
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
