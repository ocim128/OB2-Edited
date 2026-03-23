using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Flux.Native.Views.Dialogs.Wordlist;

namespace Flux.Native.Views.Dialogs.Job.Components
{
    public partial class DataPoolSection : UserControl
    {
        public DataPoolSection()
        {
            InitializeComponent();
        }

        private void SelectWordlist(object sender, RoutedEventArgs e)
        {
            var parentPage = ConfigSelectionSection.FindParent<MultiRunJobOptionsDialog>(this);
            if (parentPage != null)
            {
               new MainDialog(new SelectWordlistDialog(parentPage), "Select a wordlist").ShowDialog();
            }
        }

        private void AddWordlist(object sender, RoutedEventArgs e)
        {
            var parentPage = ConfigSelectionSection.FindParent<MultiRunJobOptionsDialog>(this);
            if (parentPage != null)
            {
                new MainDialog(new AddWordlistDialog(parentPage), "Add a wordlist").ShowDialog();
            }
        }

        private void SelectFileForDataPool(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Wordlist files | *.txt",
                FilterIndex = 1
            };

            if (ofd.ShowDialog() == true)
            {
                // We need to access the ViewModel bound to the button's Tag or DataContext
                if ((sender as Button)?.Tag is FileDataPoolOptionsViewModel vm)
                {
                    vm.FileName = ofd.FileName;
                }
            }
        }
    }
}
