using System;
using System.Windows;
using System.Windows.Controls;
using ConfigDialogs = Flux.Native.Views.Dialogs.Config;
using Flux.Native.Controls;

namespace Flux.Native.Views.Dialogs.Job.Components
{
    public partial class ConfigSelectionSection : UserControl
    {
        public ConfigSelectionSection()
        {
            InitializeComponent();
        }

        private void SelectConfig(object sender, RoutedEventArgs e)
        {
            // We need to access the parent page to pass as 'Owner' for the dialog
            // or just center on screen. The MainDialog requires an owner page.
            
            var parentDialog = Window.GetWindow(this) as Window; // This finds the top window, but MainDialog might want the Page.
            // Actually, the original code used 'this' which was the Page.
            // We can find the parent Page or use the Window.
            
            // For now, let's try to find the MultiRunJobOptionsDialog parent
            var parentPage = FindParent<MultiRunJobOptionsDialog>(this);
            
            if (parentPage != null)
            {
                 new MainDialog(new ConfigDialogs.SelectConfigDialog(parentPage), "Select a config").ShowDialog();
            }
        }

        public static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindParent<T>(parentObject);
        }
    }
}
