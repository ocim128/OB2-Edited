using Flux.Core.Services;
using Flux.Native.Services;
using Flux.Native.Enums;
using Flux.Native.Helpers;
using Flux.Native.ViewModels.Base;
using RuriLib.Models.Configs;
using RuriLib.Models.Jobs;
using System;
using System.ComponentModel;
using System.Linq;

namespace Flux.Native.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly JobManagerService jobManagerService;
    private readonly ConfigService configService;
    private readonly FluxSettingsService fluxSettingsService;
    private readonly INavigationService navigationService;

    public event Action<Config>? ConfigSelected;

    public Config? Config => configService.SelectedConfig;

    private bool isLoading;
    public bool IsLoading
    {
        get => isLoading;
        set
        {
            if (isLoading == value)
            {
                return;
            }

            isLoading = value;
            OnPropertyChanged();
        }
    }

    public bool IsConfigSelected => Config is not null;

    public MainWindowViewModel(
        JobManagerService jobManagerService,
        ConfigService configService,
        FluxSettingsService fluxSettingsService,
        INavigationService navigationService)
    {
        this.jobManagerService = jobManagerService;
        this.configService = configService;
        this.fluxSettingsService = fluxSettingsService;
        this.navigationService = navigationService;

        configService.OnConfigSelected += (_, config) =>
        {
            OnPropertyChanged(nameof(IsConfigSelected));
            ConfigSelected?.Invoke(config);
        };
    }

    public void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        var generalSettings = fluxSettingsService.Settings.GeneralSettings;

        if (generalSettings.WarnConfigNotSaved && Config?.HasUnsavedChanges() == true)
        {
            e.Cancel = !Alert.Confirm(
                "Config not saved",
                $"The config you are editing ({Config.Metadata.Name}) has unsaved changes, are you sure you want to quit?",
                nameof(generalSettings.WarnConfigNotSaved));
        }

        if (!e.Cancel && jobManagerService.Jobs.Any(static j => j.Status != JobStatus.Idle))
        {
            e.Cancel = !Alert.Confirm(
                "Job(s) running",
                "One or more jobs are still running, are you sure you want to quit?",
                "PerformConfirmationOnDestructiveActions");
        }
    }
}
