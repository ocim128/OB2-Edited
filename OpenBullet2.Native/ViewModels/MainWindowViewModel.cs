using OpenBullet2.Core.Services;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.Enums;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.ViewModels.Base;
using RuriLib.Models.Configs;
using RuriLib.Models.Jobs;
using System;
using System.ComponentModel;
using System.Linq;

namespace OpenBullet2.Native.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly JobManagerService jobManagerService;
    private readonly ConfigService configService;
    private readonly OpenBulletSettingsService openBulletSettingsService;
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
        OpenBulletSettingsService openBulletSettingsService,
        INavigationService navigationService)
    {
        this.jobManagerService = jobManagerService;
        this.configService = configService;
        this.openBulletSettingsService = openBulletSettingsService;
        this.navigationService = navigationService;

        configService.OnConfigSelected += (_, config) =>
        {
            OnPropertyChanged(nameof(IsConfigSelected));
            ConfigSelected?.Invoke(config);
        };
    }

    public void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        var generalSettings = openBulletSettingsService.Settings.GeneralSettings;

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
