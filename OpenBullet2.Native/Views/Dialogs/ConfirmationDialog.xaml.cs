using System.Windows;
using System.Windows.Controls;

namespace OpenBullet2.Native.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for ConfirmationDialog.xaml
    /// </summary>
    public partial class ConfirmationDialog : UserControl
    {
        public string Title { get; set; } = "Confirmation";
        public string Message { get; set; } = "Are you sure?";
        public bool Result { get; private set; } = false;

        private Window parentWindow;

        public ConfirmationDialog(string title = "Confirmation", string message = "Are you sure?")
        {
            InitializeComponent();
            Title = title;
            Message = message;
            DataContext = this;
        }

        public bool? ShowDialog(Window owner = null)
        {
            parentWindow = new Window
            {
                Title = Title,
                Content = this,
                Width = 450,
                Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner ?? Application.Current.MainWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false
            };

            return parentWindow.ShowDialog();
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            parentWindow?.Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            parentWindow?.Close();
        }
    }
} 
