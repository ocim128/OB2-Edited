using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Enums;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services.Navigation;
using OpenBullet2.Native.Views.Pages;
using OpenBullet2.Native.Views.Pages.Configs;
using OpenBullet2.Native.Views.Pages.Data;
using OpenBullet2.Native.Views.Pages.Settings;
using OpenBullet2.Native.Views.Pages.Tools;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace OpenBullet2.Native.Services.Commands;

public class CommandHandler : ICommandHandler
{
    private readonly INavigationHandler _navigationHandler;
    private readonly INavigationService _navigationService;
    private readonly ConfigService _configService;
    private readonly IConfigRepository _configRepository;

    public CommandHandler(
        INavigationHandler navigationHandler,
        INavigationService navigationService,
        ConfigService configService,
        IConfigRepository configRepository)
    {
        _navigationHandler = navigationHandler;
        _navigationService = navigationService;
        _configService = configService;
        _configRepository = configRepository;
    }

    public void InitializeCommandBindings(MainWindow window)
    {
        _ = window.CommandBindings.Add(new CommandBinding(CustomCommands.NewConfig, OnNewConfigExecuted, OnCanExecuteConfigCommand));
        _ = window.CommandBindings.Add(new CommandBinding(CustomCommands.OpenConfig, OnOpenConfigExecuted, OnCanExecuteConfigCommand));
        _ = window.CommandBindings.Add(new CommandBinding(CustomCommands.SaveConfig, OnSaveConfigExecuted, OnCanExecuteConfigCommand));
        _ = window.CommandBindings.Add(new CommandBinding(CustomCommands.Refresh, OnRefreshExecuted, OnCanExecuteRefreshCommand));
        _ = window.CommandBindings.Add(new CommandBinding(CustomCommands.Quit, OnQuitExecuted));
        _ = window.CommandBindings.Add(new CommandBinding(CustomCommands.ToggleSidebar, (s, e) => window.ToggleSidebar()));

        // Navigation Commands
        BindNavigationCommand(window, CustomCommands.NavigateToHome, MainWindowPage.Home);
        BindNavigationCommand(window, CustomCommands.NavigateToJobs, MainWindowPage.Jobs);
        BindNavigationCommand(window, CustomCommands.NavigateToTools, MainWindowPage.Tools);
        BindNavigationCommand(window, CustomCommands.NavigateToProxies, MainWindowPage.Proxies);
        BindNavigationCommand(window, CustomCommands.NavigateToWordlists, MainWindowPage.Wordlists);
        BindNavigationCommand(window, CustomCommands.NavigateToConfigs, MainWindowPage.Configs);
        BindNavigationCommand(window, CustomCommands.NavigateToHits, MainWindowPage.Hits);
        BindNavigationCommand(window, CustomCommands.NavigateToPlugins, MainWindowPage.Plugins);
        BindNavigationCommand(window, CustomCommands.NavigateToOBSettings, MainWindowPage.OBSettings);
        BindNavigationCommand(window, CustomCommands.NavigateToRLSettings, MainWindowPage.RLSettings);
    }

    private void OnCanExecuteConfigCommand(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = _navigationService.CurrentPageEnum is
            MainWindowPage.Configs or
            MainWindowPage.ConfigStacker or
            MainWindowPage.ConfigLoliCode or
            MainWindowPage.ConfigCSharpCode or
            MainWindowPage.ConfigMetadata or
            MainWindowPage.ConfigReadme or
            MainWindowPage.ConfigSettings;

    public void OnNewConfigExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is MainWindow window && window.CurrentPage is Configs page)
            page.Create(null, null);
    }

    private void OnOpenConfigExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is MainWindow window && window.CurrentPage is Configs page)
            page.Edit(null, null);
    }

    public void OnSaveConfigExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is MainWindow window)
        {
            if (window.CurrentPage is Configs configs)
            {
                configs.Save(null, null);
                return;
            }

            if (window.CurrentPage is ConfigEditor editor)
            {
                editor.Save(null, null);
                return;
            }
        }

        // Fallback for other pages
        if (_configService.SelectedConfig != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _configRepository.SaveAsync(_configService.SelectedConfig);
                    _configService.SelectedConfig.UpdateHashes();
                    Application.Current.Dispatcher.Invoke(() => Alert.Success("Saved", $"{_configService.SelectedConfig.Metadata.Name} was saved successfully!"));
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() => Alert.Exception(ex));
                }
            });
        }
    }

    public async void OnRefreshExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is MainWindow window)
        {
            if (window.CurrentPage is Configs configs)
            {
                configs.Rescan(null, null);
            }
            else if (window.CurrentPage is Hits hits)
            {
                await hits.Refresh();
            }
            else if (window.CurrentPage is Proxies proxies)
            {
                await proxies.Refresh();
            }
            else if (window.CurrentPage is Wordlists wordlists)
            {
                await wordlists.Refresh();
            }
            else if (window.CurrentPage is Plugins plugins)
            {
                plugins.Refresh();
            }
        }
    }

    private void OnCanExecuteRefreshCommand(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = _navigationService.CurrentPageEnum is
            MainWindowPage.Configs or
            MainWindowPage.Hits or
            MainWindowPage.Proxies or
            MainWindowPage.Wordlists or
            MainWindowPage.Plugins;

    private void OnQuitExecuted(object sender, ExecutedRoutedEventArgs e) => Application.Current.Shutdown();

    private void BindNavigationCommand(MainWindow window, ICommand command, MainWindowPage page)
    {
        _ = window.CommandBindings.Add(new CommandBinding(command, (s, e) => _navigationHandler.NavigateTo(page)));
    }
}
