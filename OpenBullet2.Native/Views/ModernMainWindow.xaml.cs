using System;
using System.Windows;
using System.Windows.Controls;
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

        // Navigation event handlers
        private void OpenHomePage(object sender, RoutedEventArgs e) => NavigateToPage("Home");
        private void OpenJobsPage(object sender, RoutedEventArgs e) => NavigateToPage("Jobs");
        private void OpenMonitorPage(object sender, RoutedEventArgs e) => NavigateToPage("Monitor");
        private void OpenProxiesPage(object sender, RoutedEventArgs e) => NavigateToPage("Proxies");
        private void OpenWordlistsPage(object sender, RoutedEventArgs e) => NavigateToPage("Wordlists");
        private void OpenConfigsPage(object sender, RoutedEventArgs e) => NavigateToPage("Configs");
        private void OpenHitsPage(object sender, RoutedEventArgs e) => NavigateToPage("Hits");
        private void OpenPluginsPage(object sender, RoutedEventArgs e) => NavigateToPage("Plugins");
        private void OpenOBSettingsPage(object sender, RoutedEventArgs e) => NavigateToPage("OBSettings");
        private void OpenRLSettingsPage(object sender, RoutedEventArgs e) => NavigateToPage("RLSettings");
        private void OpenAboutPage(object sender, RoutedEventArgs e) => NavigateToPage("About");

        // Config submenu handlers
        private void OpenMetadataPage(object sender, RoutedEventArgs e) => NavigateToPage("ConfigMetadata");
        private void OpenReadmePage(object sender, RoutedEventArgs e) => NavigateToPage("ConfigReadme");
        private void OpenStackerPage(object sender, RoutedEventArgs e) => NavigateToPage("Stacker");
        private void OpenLoliCodePage(object sender, RoutedEventArgs e) => NavigateToPage("LoliCode");
        private void OpenConfigSettingsPage(object sender, RoutedEventArgs e) => NavigateToPage("ConfigSettings");
        private void OpenCSharpCodePage(object sender, RoutedEventArgs e) => NavigateToPage("CSharpCode");
        private void OpenLoliScriptPage(object sender, RoutedEventArgs e) => NavigateToPage("LoliScript");

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
        private void NavigateToPage(string pageName)
        {
            MessageBox.Show($"Navigation to {pageName} page not yet implemented in modernization demo.\n\n" +
                           "This is a demonstration of the modern UI design. " +
                           "Full integration would connect these buttons to the existing pages.", 
                           "Navigation Demo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
} 