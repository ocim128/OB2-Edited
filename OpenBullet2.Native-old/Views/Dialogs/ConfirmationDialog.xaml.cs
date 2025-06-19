using OpenBullet2.Native.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace OpenBullet2.Native.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for ConfirmationDialog.xaml
    /// </summary>
    public partial class ConfirmationDialog : Page
    {
        private readonly ConfirmationDialogViewModel vm;
        private readonly Action<bool, bool> onResult;

        public ConfirmationDialog(string title, string message, Action<bool, bool> onResult,
            string yesText = "Yes", string noText = "No")
        {
            vm = new ConfirmationDialogViewModel(title, message, yesText, noText);
            DataContext = vm;
            this.onResult = onResult;

            InitializeComponent();
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            onResult.Invoke(true, doNotAskAgainCheckbox.IsChecked.GetValueOrDefault());
            ((MainDialog)Parent).Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            onResult.Invoke(false, doNotAskAgainCheckbox.IsChecked.GetValueOrDefault());
            ((MainDialog)Parent).Close();
        }
    }

    public class ConfirmationDialogViewModel : ViewModelBase
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public string YesText { get; set; }
        public string NoText { get; set; }

        public ConfirmationDialogViewModel(string title, string message, string yesText, string noText)
        {
            Title = title;
            Message = message;
            YesText = yesText;
            NoText = noText;
        }
    }
} 