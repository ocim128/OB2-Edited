using System.Windows;
using System.Windows.Controls;
using Flux.Native.Enums;
using Flux.Native.ViewModels.Pages;

namespace Flux.Native.Views.Pages.Home
{
    /// <summary>
    /// Interaction logic for Home.xaml
    /// </summary>
    public partial class Home : Page
    {
        private readonly MainWindow _mainWindow;
        private readonly HomeViewModel _viewModel;

        public Home(MainWindow mainWindow, HomeViewModel viewModel)
        {
            _mainWindow = mainWindow;
            _viewModel = viewModel;

            InitializeComponent();
            DataContext = _viewModel;

            Loaded += (_, _) => _viewModel.Resume();
            Unloaded += (_, _) => _viewModel.Suspend();
        }

        private void ConfigsShortcut_Click(object sender, RoutedEventArgs e)
            => _mainWindow.NavigateTo(MainWindowPage.Configs);

        private void JobsShortcut_Click(object sender, RoutedEventArgs e)
            => _mainWindow.NavigateTo(MainWindowPage.Jobs);

        private void WordlistsShortcut_Click(object sender, RoutedEventArgs e)
            => _mainWindow.NavigateTo(MainWindowPage.Wordlists);

        private void HitsShortcut_Click(object sender, RoutedEventArgs e)
            => _mainWindow.NavigateTo(MainWindowPage.Hits);
    }
}
