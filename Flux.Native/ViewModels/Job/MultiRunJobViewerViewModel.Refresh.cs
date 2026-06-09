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
        if (disposed)
        {
            return;
        }

        if (!await refreshLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (disposed)
            {
                return;
            }

            var latest = await jobOrchestrator.GetMultiRunJobViewerSnapshotAsync(Job.Id).ConfigureAwait(false);
            if (latest is null || disposed)
            {
                return;
            }

            await ApplySnapshotOnUiThreadAsync(latest, refreshResults: forceResultsRefresh || latest.Results.Count != allResults.Count)
                .ConfigureAwait(false);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private async Task ApplySnapshotOnUiThreadAsync(MultiRunJobViewerSnapshotDto latest, bool refreshResults)
    {
        if (disposed)
        {
            return;
        }

        if (Application.Current?.Dispatcher is null || Application.Current.Dispatcher.CheckAccess())
        {
            ApplySnapshot(latest, refreshResults);
            return;
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!disposed)
            {
                ApplySnapshot(latest, refreshResults);
            }
        });
    }

    private void ApplySnapshot(MultiRunJobViewerSnapshotDto latest, bool refreshResults)
    {
        if (disposed)
        {
            return;
        }

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

        var currentSnapshot = snapshot;
        if (currentSnapshot == null) return;

        // Only fire PropertyChanged for properties that actually changed value.
        // Computed properties derive from snapshot/Job, so we compare their current output.
        OnPropertyChanged(nameof(ConfigNameAndAuthor));
        OnPropertyChanged(nameof(ConfigName));
        OnPropertyChanged(nameof(ConfigAuthor));
        OnPropertyChanged(nameof(DataPoolInfo));
        OnPropertyChanged(nameof(ProxySourcesInfo));
        OnPropertyChanged(nameof(HitOutputsInfo));
        OnPropertyChanged(nameof(CustomInputsInfo));
        OnPropertyChanged(nameof(HasCustomInputs));
        OnPropertyChanged(nameof(RemainingWaitString));

        // Boolean status flags -- cheap to evaluate, fire all (they're used for CanExecute)
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
        RunOnUiThread(() => SyncBotCollection(BotsCollection, bots));
    }

    private static void SyncBotCollection(ObservableCollection<BotViewModel> collection, IReadOnlyList<BotStateDto> target)
    {
        var existingById = collection.ToDictionary(static bot => bot.Id);
        var targetViewModels = new List<BotViewModel>(target.Count);

        foreach (var snapshot in target)
        {
            if (existingById.TryGetValue(snapshot.Id, out var botViewModel))
            {
                botViewModel.ApplySnapshot(snapshot);
            }
            else
            {
                botViewModel = new BotViewModel(snapshot);
            }

            targetViewModels.Add(botViewModel);
        }

        SyncCollection(collection, targetViewModels);
    }

    private static void SyncCollection<T>(ObservableCollection<T> collection, List<T> target) where T : notnull
    {
        var i = 0;
        while (i < Math.Min(collection.Count, target.Count))
        {
            if (!EqualityComparer<T>.Default.Equals(collection[i], target[i]))
            {
                var existingIndex = collection.IndexOf(target[i]);
                if (existingIndex >= 0)
                {
                    while (i < existingIndex)
                    {
                        collection.RemoveAt(i);
                        existingIndex--;
                    }
                }
                else
                {
                    collection.Insert(i, target[i]);
                    i++;
                }
            }
            else
            {
                i++;
            }
        }

        while (collection.Count > target.Count)
            collection.RemoveAt(collection.Count - 1);

        while (collection.Count < target.Count)
            collection.Add(target[collection.Count]);
    }

    private void RunOnUiThread(Action action)
    {
        if (disposed)
        {
            return;
        }

        if (Application.Current?.Dispatcher is null || Application.Current.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!disposed)
            {
                action();
            }
        });
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        refreshTimer?.Dispose();
        soundPlayer?.Dispose();
    }
}

public class BotViewModel : ViewModelBase
{
    private BotStateDto bot;

    public BotViewModel(BotStateDto bot)
    {
        this.bot = bot;
    }

    public int Id => bot.Id;
    public string Data => bot.Data;
    public string Proxy => bot.Proxy;
    public string Info => bot.Info;

    public void ApplySnapshot(BotStateDto snapshot)
    {
        bot = snapshot;
        OnPropertyChanged(nameof(Data));
        OnPropertyChanged(nameof(Proxy));
        OnPropertyChanged(nameof(Info));
    }
}
