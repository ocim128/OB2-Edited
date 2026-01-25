using MahApps.Metro.IconPacks;
using OpenBullet2.Native.ViewModels.Base;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenBullet2.Native.Views.Dialogs.Common
{
    /// <summary>
    /// Interaction logic for AlertDialog.xaml
    /// </summary>
    public partial class AlertDialog : Page
    {
        public AlertDialog(AlertType type, string title, string message)
        {
            InitializeComponent();

            this.title.Text = title;
            
            // Check message length and use appropriate display method
            if (message.Length > 200 || message.Contains("\n"))
            {
                // Long message - use scrollable text block
                this.message.Visibility = Visibility.Collapsed;
                this.messageScrollViewer.Visibility = Visibility.Visible;
                this.messageScrollable.Text = message;
            }
            else
            {
                // Short message - use regular text block
                this.message.Text = message;
                this.messageScrollViewer.Visibility = Visibility.Collapsed;
            }

            icon.Kind = type switch
            {
                AlertType.Success => PackIconOcticonsKind.Check,
                AlertType.Warning => PackIconOcticonsKind.Alert,
                AlertType.Error => PackIconOcticonsKind.X,
                AlertType.Info => PackIconOcticonsKind.Info,
                _ => throw new NotImplementedException()
            };

            icon.Foreground = type switch
            {
                AlertType.Success => Brushes.YellowGreen,
                AlertType.Warning => Brushes.Orange,
                AlertType.Error => Brushes.Tomato,
                AlertType.Info => Brushes.SkyBlue,
                _ => throw new NotImplementedException()
            };

            okButton.Focus();
        }

        private void Ok(object sender, RoutedEventArgs e) => UIHelpers.CloseParentDialog(this);

        private void PageKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                UIHelpers.CloseParentDialog(this);
            }
        }
    }

    public enum AlertType
    {
        Success,
        Warning,
        Error,
        Info
    }
}
