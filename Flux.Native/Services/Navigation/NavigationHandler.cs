using Flux.Native.Enums;
using Flux.Native.Factories;
using Flux.Native.Services.Menu;
using Flux.Native.ViewModels;
using Flux.Native.ViewModels.Jobs;
using Flux.Native.Views.Pages;
using Flux.Native.Views.Pages.Jobs;
using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using Flux.Native.Services;

namespace Flux.Native.Services.Navigation;

public class NavigationHandler : INavigationHandler
{
    private readonly INavigationService _navigationService;
    private readonly PageButtonMapper _buttonMapper;
    private readonly MainWindowViewModel _viewModel;
    private readonly IMenuHandler _menuHandler;
    private readonly IPageFactory _pageFactory;
    private Button _currentSelectedButton;
    private System.Windows.Controls.Page _transientPage;

    public System.Windows.Controls.Page CurrentPage => _transientPage ?? _navigationService.CurrentPage;

    public event EventHandler<NavigationEventArgs> Navigated;

    public NavigationHandler(
        INavigationService navigationService,
        PageButtonMapper buttonMapper,
        MainWindowViewModel viewModel,
        IMenuHandler menuHandler,
        IPageFactory pageFactory)
    {
        _navigationService = navigationService;
        _buttonMapper = buttonMapper;
        _viewModel = viewModel;
        _menuHandler = menuHandler;
        _pageFactory = pageFactory;

        _navigationService.Navigated += OnNavigationServiceNavigated;
    }

    public Task NavigateTo(MainWindowPage page)
    {
        _viewModel.IsLoading = true;
        _navigationService.NavigateTo(page);
        return Task.CompletedTask;
    }

    public void DisplayJob(JobViewModel jobVM)
    {
        switch (jobVM)
        {
            case MultiRunJobViewModel mrj:
                var mrjPage = _pageFactory.CreateMultiRunJobViewerPage();
                mrjPage.BindViewModel(mrj);
                ChangePage(mrjPage, null);
                break;

            case ProxyCheckJobViewModel pcj:
                var pcjPage = _pageFactory.CreateProxyCheckJobViewerPage();
                pcjPage.BindViewModel(pcj);
                ChangePage(pcjPage, null);
                break;
        }
    }

    public void EditJob(JobViewModel jobVM)
    {
        NavigateTo(MainWindowPage.Jobs);
        if (_navigationService.CurrentPage is Jobs initialJobsPage)
        {
            initialJobsPage.EditJob(jobVM);
        }
    }

    private void OnNavigationServiceNavigated(object sender, NavigationEventArgs e)
    {
        _transientPage = null;
        _menuHandler.UpdateMenuHighlight(e.PageEnum);
        _viewModel.IsLoading = false;
        Navigated?.Invoke(this, e);
    }

    private void ChangePage(Page newPage, Button newButton)
    {
        _transientPage = newPage;
        _menuHandler.UpdateButtonHighlight(_currentSelectedButton, newButton);
        _currentSelectedButton = newButton;
        _viewModel.IsLoading = false;
        
        // We need to trigger the Navigated event even for transient pages if we want MainWindow to update
        Navigated?.Invoke(this, new NavigationEventArgs(newPage, MainWindowPage.JobViewer));
    }
}
