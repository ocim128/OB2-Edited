using System;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using MahApps.Metro.Controls;

namespace OpenBullet2.Native.Views
{
    public partial class ModernMainWindow : MetroWindow
    {
        private bool hoveringConfigSubmenu = false;
        private bool hoveringConfigsMenuOption = false;

        public ModernMainWindow()
        {
            InitializeComponent();

            // Handle window state changes to set minimum size on restore
            this.StateChanged += OnWindowStateChanged;
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
            {
                // When restored from maximized, set to minimum size
                this.Width = this.MinWidth;
                this.Height = this.MinHeight;
            }
        }

        // Consolidated navigation handlers - reduced from 18 methods to 1
        private void HandleNavigation(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            var pageName = button?.Name switch
            {
                "navHome" => "Home",
                "navJobs" => "Jobs",
                "navMonitor" => "Monitor",
                "navProxies" => "Proxies",
                "navWordlists" => "Wordlists",
                "navConfigs" => "Configs",
                "navHits" => "Hits",
                "navPlugins" => "Plugins",
                "navOBSettings" => "OBSettings",
                "navRLSettings" => "RLSettings",
                "navAbout" => "About",
                "menuOptionMetadata" => "ConfigMetadata",
                "menuOptionReadme" => "ConfigReadme",
                "menuOptionStacker" => "Stacker",
                "menuOptionLoliCode" => "LoliCode",
                "menuOptionConfigSettings" => "ConfigSettings",
                "menuOptionCSharpCode" => "CSharpCode",
                "menuOptionLoliScript" => "LoliScript",
                _ => "Home"
            };
            NavigateToPage(pageName);
        }

        // Config submenu hover logic
        private void ConfigsMenuOptionMouseEnter(object sender, MouseEventArgs e)
        {
            // For demo purposes, assume config is always selected
            // In real implementation, check if a config is actually selected
            hoveringConfigsMenuOption = true;
            UpdateConfigSubmenuPosition();
            configSubmenu.Visibility = Visibility.Visible;
        }

        private async void ConfigsMenuOptionMouseLeave(object sender, MouseEventArgs e)
        {
            hoveringConfigsMenuOption = false;
            await CheckCloseSubmenuAsync();
        }

        private void ConfigSubmenuMouseEnter(object sender, MouseEventArgs e)
        {
            hoveringConfigSubmenu = true;
            configSubmenu.Visibility = Visibility.Visible;
        }

        private async void ConfigSubmenuMouseLeave(object sender, MouseEventArgs e)
        {
            hoveringConfigSubmenu = false;
            await CheckCloseSubmenuAsync();
        }

        private async Task CheckCloseSubmenuAsync()
        {
            await Task.Delay(100);

            if (!hoveringConfigSubmenu && !hoveringConfigsMenuOption)
            {
                configSubmenu.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateConfigSubmenuPosition()
        {
            try
            {
                // Get the position of the Configs button relative to the main grid
                var configsPosition = navConfigs.TransformToAncestor(this).Transform(new Point(0, 0));

                // Position the submenu to the right of the sidebar and aligned with the Configs button
                configSubmenu.Margin = new Thickness(
                    260 + 8, // sidebar width + small gap
                    configsPosition.Y,
                    0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating submenu position: {ex.Message}");
                // Fallback positioning
                configSubmenu.Margin = new Thickness(268, 200, 0, 0);
            }
        }

        // Action handlers
        private void TakeScreenshot(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Screenshot functionality not yet implemented in modernization demo.",
                           "Feature Demo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowUpdateConfirmation(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Update check functionality not yet implemented in modernization demo.",
                           "Feature Demo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Helper method for navigation
        private static void NavigateToPage(string pageName)
        {
            MessageBox.Show($"Navigation to {pageName} page not yet implemented in modernization demo.\n\n" +
                           "This is a demonstration of the modern UI design. " +
                           "Full integration would connect these buttons to the existing pages.",
                           "Navigation Demo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}