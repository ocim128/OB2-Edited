using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Flux.Native.Utils;
using Flux.Native.ViewModels.Base;
using Flux.Shared.Models;
using RuriLib.Models.Jobs;

namespace Flux.Native.ViewModels.Jobs;

public partial class MultiRunJobViewerViewModel
{
    private async Task RefreshAsync(bool forceResultsRefresh = false)
    {
        if (!await refreshLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var latest = await jobQueries.GetMultiRunJobViewerSnapshotAsync(Job.Id).ConfigureAwait(false);
            if (latest is null)
            {
                return;
            }

            ApplySnapshot(latest, refreshResults: forceResultsRefresh || latest.Results.Count != allResults.Count);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private void ApplySnapshot(MultiRunJobViewerSnapshotDto latest, bool refreshResults)
    {
        var previousHits = snapshot?.Summary.DataHits ?? 0;
        snapshot = latest;
        Job.ApplySnapshot(latest.Summary);
        customInputAnswers = latest.CustomInputAnswers.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value);

        if (lastConfigIconBase64 != latest.ConfigIconBase64)
        {
            lastConfigIconBase64 = latest.ConfigIconBase64 ?? string.Empty;
            ConfigIcon = string.IsNullOrWhiteSpace(lastConfigIconBase64)
                ? null
                : Images.Base64ToBitmapImage(lastConfigIconBase64);
            OnPropertyChanged(nameof(ConfigIcon));
        }

        UpdateBots(latest.Bots);

        if (refreshResults)
        {
            allResults = latest.Results;
            UpdateHitsCollection();
        }

        if (fluxSettingsService.Settings.CustomizationSettings.PlaySoundOnHit && latest.Summary.DataHits > lastSoundHitsCount)
        {
            TryPlayHitSound();
        }

        lastSoundHitsCount = latest.Summary.DataHits;

        if (latest.Summary.Status is JobStatus.Running)
        {
            RecordSparklineData();
        }

        NotifyStateChanged();

        if (latest.Summary.DataHits != previousHits)
        {
            OnPropertyChanged(nameof(HitsCount));
            OnPropertyChanged(nameof(HitsTabLabel));
        }
    }

    private void NotifyStateChanged()
    {
        Job.UpdateViewModel();
        Job.UpdateStats();
        Job.PeriodicUpdate();

        OnPropertyChanged(nameof(ConfigNameAndAuthor));
        OnPropertyChanged(nameof(ConfigName));
        OnPropertyChanged(nameof(ConfigAuthor));
        OnPropertyChanged(nameof(DataPoolInfo));
        OnPropertyChanged(nameof(ProxySourcesInfo));
        OnPropertyChanged(nameof(HitOutputsInfo));
        OnPropertyChanged(nameof(CustomInputsInfo));
        OnPropertyChanged(nameof(HasCustomInputs));
        OnPropertyChanged(nameof(RemainingWaitString));
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(CanChangeOptions));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanSkipWait));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanAbort));
        OnPropertyChanged(nameof(IsStarting));
        OnPropertyChanged(nameof(IsStopping));
        OnPropertyChanged(nameof(IsPausing));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(HitsCount));
        OnPropertyChanged(nameof(CustomCount));
        OnPropertyChanged(nameof(ToCheckCount));
        OnPropertyChanged(nameof(HitsTabLabel));
        OnPropertyChanged(nameof(CustomTabLabel));
        OnPropertyChanged(nameof(ToCheckTabLabel));
        OnPropertyChanged(nameof(AnimatedCpm));
        OnPropertyChanged(nameof(AnimatedHits));
        OnPropertyChanged(nameof(AnimatedCustom));
        OnPropertyChanged(nameof(AnimatedToCheck));
        OnPropertyChanged(nameof(AnimatedBanned));
        OnPropertyChanged(nameof(AnimatedFails));
        OnPropertyChanged(nameof(AnimatedRetried));
        OnPropertyChanged(nameof(AnimatedErrors));
    }

    private void UpdateBots(IReadOnlyList<BotStateDto> bots)
    {
        var botItems = bots.Select(static bot => new BotViewModel(bot)).ToList();
        RunOnUiThread(() =>
        {
            BotsCollection.Clear();
            foreach (var bot in botItems)
                BotsCollection.Add(bot);
        });
    }

    private static void RunOnUiThread(Action action)
    {
        if (Application.Current?.Dispatcher is null || Application.Current.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        try
        {
            refreshTimer?.Dispose();
            soundPlayer?.Dispose();
            refreshLock?.Dispose();
            botChangeLock?.Dispose();
        }
        catch
        {
        }
    }
}

public class BotViewModel : ViewModelBase
{
    private readonly BotStateDto bot;

    public BotViewModel(BotStateDto bot)
    {
        this.bot = bot;
    }

    public int Id => bot.Id;
    public string Data => bot.Data;
    public string Proxy => bot.Proxy;
    public string Info => bot.Info;
}
