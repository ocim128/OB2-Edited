using OpenBullet2.Core.Entities;
using OpenBullet2.Core.Models.Hits;
using OpenBullet2.Core.Models.Proxies.Sources;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Helpers;

using OpenBullet2.Native.Utils;
using RuriLib.Extensions;
using RuriLib.Models.Bots;
using RuriLib.Models.Data.DataPools;
using RuriLib.Models.Hits;
using RuriLib.Models.Hits.HitOutputs;
using RuriLib.Models.Jobs;
using RuriLib.Models.Jobs.StartConditions;
using RuriLib.Models.Proxies.ProxySources;
using RuriLib.Parallelization.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenBullet2.Native.ViewModels.Base;
using Microsoft.Extensions.DependencyInjection;

namespace OpenBullet2.Native.ViewModels.Jobs;

public class MultiRunJobViewerViewModel : ViewModelBase, IDisposable
{
    private readonly OpenBulletSettingsService obSettingsService;
    private readonly List<ProxyGroupEntity> proxyGroups;
    private readonly Timer botsInfoTimer;
    private readonly Timer secondsTicker;
    private readonly SoundPlayer soundPlayer;
    private readonly SemaphoreSlim botChangeLock = new(1, 1); // Prevent race conditions on rapid bot changes
    private CancellationTokenSource startCTS;

    public event Action<object, string, Color> NewMessage;

    public MultiRunJobViewModel Job { get; set; }
    private MultiRunJob MultiRunJob => Job.Job as MultiRunJob;

    #region Properties that don't need to be updated during the run
    private BitmapImage configIcon;
    public BitmapImage ConfigIcon
    {
        get => configIcon;
        private set
        {
            configIcon = value;
            OnPropertyChanged();
        }
    }

    private string configNameAndAuthor;
    public string ConfigNameAndAuthor
    {
        get => configNameAndAuthor;
        set
        {
            configNameAndAuthor = value;
            OnPropertyChanged();
        }
    }

    public string ConfigName => MultiRunJob.Config?.Metadata.Name ?? "No config";
    public string ConfigAuthor => MultiRunJob.Config != null ? $"by {MultiRunJob.Config.Metadata.Author}" : "";

    public string DataPoolInfo => MultiRunJob.DataPool switch
    {
        WordlistDataPool w => $"Wordlist ({w.Wordlist.Name})",
        FileDataPool f => $"File ({f.FileName})",
        InfiniteDataPool => "Infinite",
        RangeDataPool r => $"Range (start: {r.Start}, amount: {r.Amount}, step: {r.Step}, pad: {r.Pad})",
        CombinationsDataPool c => $"Combinations (charset: {c.CharSet}, length: {c.Length})",
        _ => throw new NotImplementedException()
    };

    private string proxySourcesInfo = string.Empty;
    public string ProxySourcesInfo
    {
        get => proxySourcesInfo;
        set
        {
            proxySourcesInfo = value;
            OnPropertyChanged();
        }
    }

    private string hitOutputsInfo = string.Empty;
    public string HitOutputsInfo
    {
        get => hitOutputsInfo;
        set
        {
            hitOutputsInfo = value;
            OnPropertyChanged();
        }
    }

    public string CustomInputsInfo => string.Join(", ", MultiRunJob.CustomInputsAnswers.Select(static kvp => $"{kvp.Key}: {kvp.Value}"));
    public bool HasCustomInputs => MultiRunJob.Config?.Settings.InputSettings.CustomInputs.Any() == true;

    public bool EnableJobLog => obSettingsService.Settings.GeneralSettings.EnableJobLogging;
    #endregion Properties that don't need to be updated during the run

    #region Properties that need to be updated every second
    public string RemainingWaitString => !IsWaiting
                ? ""
                : MultiRunJob.StartCondition switch
                {
                    RelativeTimeStartCondition r => (MultiRunJob.StartTime + r.StartAfter - DateTime.Now).ToString(@"hh\:mm\:ss"),
                    AbsoluteTimeStartCondition a => (a.StartAt - DateTime.Now).ToString(@"hh\:mm\:ss"),
                    _ => ""
                };

    public bool IsWaiting => MultiRunJob.Status is JobStatus.Waiting;
    #endregion Properties that need to be updated every second

    #region Properties that need to be updated when the status changes
    public bool CanChangeOptions => MultiRunJob.Status is JobStatus.Idle;
    public bool CanStart => MultiRunJob.Status is JobStatus.Idle;
    public bool CanSkipWait => MultiRunJob.Status is JobStatus.Waiting;
    public bool CanPause => MultiRunJob.Status is JobStatus.Running;
    public bool CanResume => MultiRunJob.Status is JobStatus.Paused;
    public bool CanStop => MultiRunJob.Status is JobStatus.Running or JobStatus.Paused;
    public bool CanAbort => MultiRunJob.Status is JobStatus.Starting or JobStatus.Running or JobStatus.Paused or JobStatus.Pausing or JobStatus.Stopping;

    public bool IsStarting => MultiRunJob.Status is JobStatus.Starting;
    public bool IsStopping => MultiRunJob.Status is JobStatus.Stopping;
    public bool IsPausing => MultiRunJob.Status is JobStatus.Pausing;
    #endregion Properties that need to be updated when the status changes

    #region Properties that need to be updated when a new result comes in
    public double Progress => Math.Clamp(MultiRunJob.Progress * 100, 0, 100);
    #endregion Properties that need to be updated when a new result comes in

    #region Badge Counts for Tab Buttons
    public int HitsCount => MultiRunJob.DataHits;
    public int CustomCount => MultiRunJob.DataCustom;
    public int ToCheckCount => MultiRunJob.DataToCheck;

    public string HitsTabLabel => HitsCount > 0 ? $"Hits ({HitsCount})" : "Hits";
    public string CustomTabLabel => CustomCount > 0 ? $"Custom ({CustomCount})" : "Custom";
    public string ToCheckTabLabel => ToCheckCount > 0 ? $"ToCheck ({ToCheckCount})" : "ToCheck";
    #endregion Badge Counts for Tab Buttons

    #region Real-time Statistics Visualization
    private const int MaxHistoryPoints = 30;
    
    // CPM History for sparkline
    private readonly List<double> cpmHistory = new();
    public IReadOnlyList<double> CpmHistory => cpmHistory;
    
    // Hits per minute history
    private readonly List<double> hitsPerMinuteHistory = new();
    public IReadOnlyList<double> HitsPerMinuteHistory => hitsPerMinuteHistory;
    
    private int lastRecordedHits = 0;
    private DateTime lastHitsRecordTime = DateTime.Now;
    
    /// <summary>
    /// Animated counter values for smooth transitions
    /// </summary>
    public double AnimatedCpm => Job?.CPM ?? 0;
    public double AnimatedHits => Job?.Job is MultiRunJob mrj ? mrj.DataHits : 0;
    public double AnimatedCustom => Job?.Job is MultiRunJob mrj ? mrj.DataCustom : 0;
    public double AnimatedToCheck => Job?.Job is MultiRunJob mrj ? mrj.DataToCheck : 0;
    public double AnimatedBanned => Job?.Job is MultiRunJob mrj ? mrj.DataBanned : 0;
    public double AnimatedFails => Job?.Job is MultiRunJob mrj ? mrj.DataFails : 0;
    public double AnimatedRetried => Job?.Job is MultiRunJob mrj ? mrj.DataRetried : 0;
    public double AnimatedErrors => Job?.Job is MultiRunJob mrj ? mrj.DataErrors : 0;
    
    /// <summary>
    /// Event fired when sparkline data is updated
    /// </summary>
    public event Action SparklineDataUpdated;
    
    /// <summary>
    /// Records current stats for sparkline history. Called every second.
    /// </summary>
    private void RecordSparklineData()
    {
        if (MultiRunJob == null) return;
        
        // Record CPM
        cpmHistory.Add(MultiRunJob.CPM);
        while (cpmHistory.Count > MaxHistoryPoints)
            cpmHistory.RemoveAt(0);
        
        // Calculate and record hits per minute
        var now = DateTime.Now;
        var elapsed = (now - lastHitsRecordTime).TotalMinutes;
        if (elapsed > 0)
        {
            var currentHits = MultiRunJob.DataHits;
            var hitsDelta = currentHits - lastRecordedHits;
            var hitsPerMinute = hitsDelta / elapsed;
            
            hitsPerMinuteHistory.Add(Math.Max(0, hitsPerMinute));
            while (hitsPerMinuteHistory.Count > MaxHistoryPoints)
                hitsPerMinuteHistory.RemoveAt(0);
            
            lastRecordedHits = currentHits;
            lastHitsRecordTime = now;
        }
        
        // Notify UI to update sparklines
        SparklineDataUpdated?.Invoke();
        
        // Update animated counter bindings
        OnPropertyChanged(nameof(AnimatedCpm));
        OnPropertyChanged(nameof(AnimatedHits));
        OnPropertyChanged(nameof(AnimatedCustom));
        OnPropertyChanged(nameof(AnimatedToCheck));
        OnPropertyChanged(nameof(AnimatedBanned));
        OnPropertyChanged(nameof(AnimatedFails));
        OnPropertyChanged(nameof(AnimatedRetried));
        OnPropertyChanged(nameof(AnimatedErrors));
    }
    
    /// <summary>
    /// Clears all sparkline history data. Call when starting a new job.
    /// </summary>
    public void ClearSparklineData()
    {
        cpmHistory.Clear();
        hitsPerMinuteHistory.Clear();
        lastRecordedHits = 0;
        lastHitsRecordTime = DateTime.Now;
        SparklineDataUpdated?.Invoke();
    }
    #endregion Real-time Statistics Visualization

    #region Collections
    private ObservableCollection<BotViewModel> botsCollection = new();
    public ObservableCollection<BotViewModel> BotsCollection
    {
        get => botsCollection;
        set
        {
            botsCollection = value;
            OnPropertyChanged();
        }
    }

    private ObservableCollection<HitViewModel> hitsCollection = new();
    public ObservableCollection<HitViewModel> HitsCollection
    {
        get => hitsCollection;
        set
        {
            hitsCollection = value;
            OnPropertyChanged();
        }
    }

    public static IEnumerable<HitsFilter> HitsFilters => Enum.GetValues(typeof(HitsFilter)).Cast<HitsFilter>();

    private HitsFilter hitsFilter = HitsFilter.Hits;
    public HitsFilter HitsFilter
    {
        get => hitsFilter;
        set
        {
            hitsFilter = value;
            OnPropertyChanged();
            UpdateHitsCollection();
        }
    }

    private string searchQuery = string.Empty;
    public string SearchQuery
    {
        get => searchQuery;
        set
        {
            if (searchQuery == value)
            {
                return;
            }

            searchQuery = value;
            OnPropertyChanged();
            UpdateHitsCollection();
        }
    }

    #endregion Collections

    public MultiRunJobViewerViewModel(MultiRunJobViewModel jobVM)
    {
        obSettingsService = App.ServiceProvider.GetRequiredService<OpenBulletSettingsService>();
        Job = jobVM;

        #region Setup
        if (MultiRunJob.Config is not null)
        {
            ConfigIcon = Images.Base64ToBitmapImage(MultiRunJob.Config.Metadata.Base64Image);
            ConfigNameAndAuthor = $"{MultiRunJob.Config.Metadata.Name} by {MultiRunJob.Config.Metadata.Author}";
        }

        var proxyGroupRepo = App.ServiceProvider.GetRequiredService<IProxyGroupRepository>();
        proxyGroups = [.. proxyGroupRepo.GetAll()];

        var sb = new StringBuilder();
        for (var i = 0; i < MultiRunJob.ProxySources.Count; i++)
        {
            var info = MultiRunJob.ProxySources[i] switch
            {
                GroupProxySource g => $"Group ({GetProxyGroupName(g.GroupId)})",
                FileProxySource f => $"File ({f.FileName})",
                RemoteProxySource r => $"Remote ({r.Url})",
                _ => throw new NotImplementedException()
            };

            _ = sb.Append(info);

            if (i < MultiRunJob.ProxySources.Count - 1)
            {
                _ = sb.Append(" | ");
            }
        }

        ProxySourcesInfo = sb.ToString();

        sb = new StringBuilder();
        for (var i = 0; i < MultiRunJob.HitOutputs.Count; i++)
        {
            var info = MultiRunJob.HitOutputs[i] switch
            {
                DatabaseHitOutput => "Database",
                FileSystemHitOutput fs => $"File System ({fs.BaseDir})",
                DiscordWebhookHitOutput d => $"Discord ({d.Webhook.TruncatePretty(70)})",
                TelegramBotHitOutput t => $"Telegram ({t.Token.Split(':')[0]})",
                CustomWebhookHitOutput c => $"Custom Webhook ({c.Url.TruncatePretty(70)})",
                _ => throw new NotImplementedException()
            };

            _ = sb.Append(info);

            if (i < MultiRunJob.HitOutputs.Count - 1)
            {
                _ = sb.Append(" | ");
            }
        }

        HitOutputsInfo = sb.ToString();
        #endregion Setup

        #region Bind events and timers
        MultiRunJob.OnCompleted += UpdateOnCompleted;
        MultiRunJob.OnResult += UpdateViewModel;
        MultiRunJob.OnStatusChanged += UpdateStatus;
        MultiRunJob.OnProgress += UpdateViewModel;

        MultiRunJob.OnResult += OnResult;
        MultiRunJob.OnResult += PlayHitSound;
        MultiRunJob.OnTaskError += OnTaskError;
        MultiRunJob.OnError += OnError;
        MultiRunJob.OnHit += OnHit;

        // Timer intervals optimized for performance:
        // - 500ms for bot info (was 200ms) - reduces PropertyChanged events
        // - 1000ms for periodic updates remains the same
        botsInfoTimer = new Timer(new TimerCallback(_ => RefreshBotsInfo()), null, 500, 500);
        secondsTicker = new Timer(new TimerCallback(_ => PeriodicUpdate()), null, 1000, 1000);
        soundPlayer = new SoundPlayer("Sounds/hit.wav");
        #endregion Bind events and timers

        UpdateBots();
        UpdateHitsCollection();
    }

    #region Update methods
    /// <summary>
    /// Updates the VM of all the current BotViewModel instances
    /// Only updates when job is actively running to save CPU
    /// </summary>
    private void RefreshBotsInfo()
    {
        // Skip updates when job is not running to save CPU cycles
        var status = MultiRunJob?.Status;
        if (status is not (JobStatus.Running or JobStatus.Starting or JobStatus.Pausing or JobStatus.Stopping))
        {
            return;
        }

        if (BotsCollection is not null)
        {
            foreach (var bot in BotsCollection)
            {
                bot.UpdateViewModel();
            }
        }
    }

    /// <summary>
    /// Periodic update for stuff that needs to be updated every second
    /// </summary>
    private void PeriodicUpdate()
    {
        OnPropertyChanged(nameof(IsWaiting));

        if (MultiRunJob.Status == JobStatus.Waiting)
        {
            OnPropertyChanged(nameof(RemainingWaitString));
        }

        Job.PeriodicUpdate();

        // Update the bots collection if the number of bots was changed
        if (BotsCollection is not null && BotsCollection.Count != MultiRunJob.Bots)
        {
            UpdateBots();
        }
        
        // Record stats for sparkline visualization when running
        if (MultiRunJob.Status is JobStatus.Running)
        {
            RecordSparklineData();
        }
    }

    /// <summary>
    /// Updates everything (only when a job completes, just to be safe, not expensive)
    /// </summary>
    private void UpdateOnCompleted(object? sender, EventArgs e) => UpdateViewModel();

    /// <summary>
    /// Updates the stats after every successful check
    /// </summary>
    private void UpdateViewModel(object? sender, ResultDetails<MultiRunInput, CheckResult> details)
    {
        OnPropertyChanged(nameof(Progress));
        
        // Update badge counts for tabs
        OnPropertyChanged(nameof(HitsCount));
        OnPropertyChanged(nameof(CustomCount));
        OnPropertyChanged(nameof(ToCheckCount));
        OnPropertyChanged(nameof(HitsTabLabel));
        OnPropertyChanged(nameof(CustomTabLabel));
        OnPropertyChanged(nameof(ToCheckTabLabel));
        
        Job.UpdateStats();
    }

    /// <summary>
    /// Update the stuff related to a job's status change
    /// </summary>
    private void UpdateStatus(object? sender, JobStatus status)
    {
        Job.UpdateStatus();

        OnPropertyChanged(nameof(CanChangeOptions));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanSkipWait));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanAbort));

        OnPropertyChanged(nameof(IsStarting));
        OnPropertyChanged(nameof(IsStopping));
        OnPropertyChanged(nameof(IsPausing));
    }

    private void UpdateViewModel(object? sender, float progress) => UpdateViewModel();

    private void OnHit(object? sender, Hit hit)
    {
        // Only add hits that match the current filter to avoid performance issues
        var shouldAdd = HitsFilter switch
        {
            HitsFilter.Hits => hit.Type == "SUCCESS",
            HitsFilter.ToCheck => hit.Type == "NONE",
            HitsFilter.Custom => hit.Type is not "SUCCESS" and not "NONE" and not "FAIL",
            _ => false
        };

        var query = SearchQuery;
        var matchesSearch = string.IsNullOrWhiteSpace(query) || HitMatchesSearch(hit, query);

        if (shouldAdd && matchesSearch)
        {
            Application.Current.Dispatcher.Invoke(() => HitsCollection?.Add(new HitViewModel(hit)));
        }
    }

    /// <summary>
    /// Call this at the start and when bots are changed
    /// </summary>
    private void UpdateBots()
    {
        var bots = Enumerable.Range(0, MultiRunJob.Bots)
            .Select(i => new BotViewModel(i, MultiRunJob.CurrentBotDatas));

        BotsCollection = new ObservableCollection<BotViewModel>(bots);
    }

    private void UpdateHitsCollection()
    {
        try
        {
            // Take a snapshot of the hits collection to avoid threading issues
            var hitsSnapshot = MultiRunJob.Hits.ToList();

            var hits = HitsFilter switch
            {
                HitsFilter.Hits => hitsSnapshot.Where(static h => h?.Type == "SUCCESS"),
                HitsFilter.ToCheck => hitsSnapshot.Where(static h => h?.Type == "NONE"),
                HitsFilter.Custom => hitsSnapshot.Where(static h => h != null && h.Type != "SUCCESS" && h.Type != "NONE" && h.Type != "FAIL"),
                _ => throw new NotImplementedException()
            };

            var filteredHits = ApplySearchFilter(hits);

            HitsCollection = new ObservableCollection<HitViewModel>(filteredHits.Select(static h => new HitViewModel(h)));
        }
        catch (InvalidOperationException)
        {
            // Collection was modified during enumeration, retry once
            try
            {
                var hitsSnapshot = MultiRunJob.Hits.ToList();

                var hits = HitsFilter switch
                {
                    HitsFilter.Hits => hitsSnapshot.Where(static h => h?.Type == "SUCCESS"),
                    HitsFilter.ToCheck => hitsSnapshot.Where(static h => h?.Type == "NONE"),
                    HitsFilter.Custom => hitsSnapshot.Where(static h => h != null && h.Type != "SUCCESS" && h.Type != "NONE" && h.Type != "FAIL"),
                    _ => throw new NotImplementedException()
                };

                var filteredHits = ApplySearchFilter(hits);

                HitsCollection = new ObservableCollection<HitViewModel>(filteredHits.Select(static h => new HitViewModel(h)));
            }
            catch
            {
                // If it still fails, just set an empty collection
                HitsCollection = [];
            }
        }
    }
    #endregion Update methods

    #region Logging
    private void OnResult(object? sender, ResultDetails<MultiRunInput, CheckResult> details)
    {
        var botData = details.Result.BotData;
        var data = botData.Line.Data;
        var proxy = botData.Proxy != null
            ? $"{botData.Proxy.Host}:{botData.Proxy.Port}"
            : string.Empty;

        var message = $"Line checked ({data})({proxy}) with status {botData.STATUS}";
        var color = botData.STATUS switch
        {
            "SUCCESS" => Colors.YellowGreen,
            "FAIL" => Colors.Tomato,
            "BAN" => Colors.Plum,
            "RETRY" => Colors.Yellow,
            "ERROR" => Colors.Red,
            "NONE" => Colors.SkyBlue,
            _ => Colors.Orange
        };

        NewMessage?.Invoke(this, message, color);
    }

    private void OnTaskError(object? sender, ErrorDetails<MultiRunInput> details)
    {
        var botData = details.Item.BotData;
        var data = botData.Line.Data;
        var proxy = botData.Proxy != null
            ? $"{botData.Proxy.Host}:{botData.Proxy.Port}"
            : string.Empty;

        var message = $"Task error ({data})({proxy})! {details.Exception.Message}";
        NewMessage?.Invoke(this, message, Colors.Tomato);
    }

    private void OnError(object? sender, Exception ex)
        => NewMessage?.Invoke(this, $"Job error: {ex.Message}", Colors.Tomato);
    #endregion Logging

    private void PlayHitSound(object? sender, ResultDetails<MultiRunInput, CheckResult> details)
    {
        if (obSettingsService.Settings.CustomizationSettings.PlaySoundOnHit && details.Result.BotData.STATUS == "SUCCESS")
        {
            try
            {
                soundPlayer.Play();
            }
            catch
            {
            }
        }
    }

    #region Controls
    public async Task StartAsync()
    {
        if (MultiRunJob.Config is null)
        {
            Alert.Error(
                "Config missing",
                "This job references a config that is no longer available. Edit the job to select a valid config before starting.");
            return;
        }

        try
        {
            startCTS = new CancellationTokenSource();
            HitsCollection = [];
            AskCustomInputs();
            OnPropertyChanged(nameof(CustomInputsInfo));
            await Task.Run(async () => await MultiRunJob.Start(startCTS.Token));
            UpdateBots();
        }
        finally
        {
            startCTS?.Dispose();
        }
    }

    public Task StopAsync() => MultiRunJob.Stop();

    public async Task AbortAsync()
    {
        if (MultiRunJob.Status is JobStatus.Starting or JobStatus.Waiting)
        {
            await startCTS.CancelAsync();
            return;
        }

        await MultiRunJob.Abort();
    }

    public Task PauseAsync() => MultiRunJob.Pause();
    public Task ResumeAsync() => MultiRunJob.Resume();
    public void SkipWait() => MultiRunJob.SkipWait();

    public async Task ChangeBotsAsync(int newValue)
    {
        // TODO: Also edit the job options! So the number of bots is persisted
        if (MultiRunJob == null) return;

        await MultiRunJob.ChangeBots(newValue);
        MultiRunJob.Bots = newValue;
        Job?.UpdateBots();
    }

    /// <summary>
    /// Quick bot adjustment: increase bots by specified amount
    /// Uses a lock to prevent race conditions when clicking rapidly
    /// </summary>
    public async Task IncreaseBotsByAsync(int amount)
    {
        if (!await botChangeLock.WaitAsync(0)) return; // Skip if another change is in progress
        try
        {
            if (MultiRunJob == null) return;
            var newValue = Math.Max(1, MultiRunJob.Bots + amount);
            await ChangeBotsAsync(newValue);
        }
        finally
        {
            botChangeLock.Release();
        }
    }

    /// <summary>
    /// Quick bot adjustment: decrease bots by specified amount
    /// Uses a lock to prevent race conditions when clicking rapidly
    /// </summary>
    public async Task DecreaseBotsByAsync(int amount)
    {
        if (!await botChangeLock.WaitAsync(0)) return; // Skip if another change is in progress
        try
        {
            if (MultiRunJob == null) return;
            var newValue = Math.Max(1, MultiRunJob.Bots - amount);
            await ChangeBotsAsync(newValue);
        }
        finally
        {
            botChangeLock.Release();
        }
    }

    /// <summary>
    /// Returns all current hits as a newline-separated string for clipboard copy
    /// </summary>
    public string GetAllHitsForClipboard()
    {
        var hits = MultiRunJob.Hits
            .Where(h => h != null && h.Type == "SUCCESS")
            .Select(h => h.Data?.Data ?? "");
        return string.Join(Environment.NewLine, hits);
    }

    /// <summary>
    /// Returns all current hits with capture as a newline-separated string
    /// </summary>
    public string GetAllHitsWithCaptureForClipboard()
    {
        var hits = MultiRunJob.Hits
            .Where(h => h != null && h.Type == "SUCCESS")
            .Select(h => $"{h.Data?.Data} | {h.CapturedDataString}");
        return string.Join(Environment.NewLine, hits);
    }

    public void ResetSkip()
    {
        if (MultiRunJob.Status is JobStatus.Idle)
        {
            // Reset skip and reload data pool to reflect updated source file
            MultiRunJob.Skip = 0;
            MultiRunJob.DataPool.Reload();
            Job.UpdateSkip();
            Job.UpdateViewModel();
            OnPropertyChanged(nameof(Job.Skip));
            OnPropertyChanged(nameof(Job.ProgressString));
        }
    }
    #endregion Controls

    #region Utils
    private IEnumerable<Hit> ApplySearchFilter(IEnumerable<Hit?> hits)
    {
        var query = SearchQuery;

        if (string.IsNullOrWhiteSpace(query))
        {
            return hits.Where(static h => h is not null)!;
        }

        return hits.Where(h => h is not null && HitMatchesSearch(h, query))!;
    }

    private static bool HitMatchesSearch(Hit hit, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return ContainsIgnoreCase(hit.Data?.Data, query)
            || ContainsIgnoreCase(hit.Proxy?.ToString(), query)
            || ContainsIgnoreCase(hit.Type, query)
            || ContainsIgnoreCase(hit.CapturedDataString, query);
    }

    private static bool ContainsIgnoreCase(string? source, string query)
        => !string.IsNullOrEmpty(source) && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private void AskCustomInputs()
    {
        var customInputs = MultiRunJob.Config?.Settings.InputSettings.CustomInputs;

        if (customInputs is null || customInputs.Count == 0)
        {
            MultiRunJob.CustomInputsAnswers.Clear();
            return;
        }

        MultiRunJob.CustomInputsAnswers.Clear();

        foreach (var input in customInputs)
        {
            MultiRunJob.CustomInputsAnswers[input.VariableName] = Alert.CustomInput(input.Description, input.DefaultAnswer);
        }
    }

    private string GetProxyGroupName(int id)
    {
        try
        {
            return id == -1 ? "All" : proxyGroups.First(g => g.Id == id).Name;
        }
        catch
        {
            return "Invalid";
        }
    }
    #endregion Utils

    public void Dispose()
    {
        try
        {
            botsInfoTimer?.Dispose();
            secondsTicker?.Dispose();
            soundPlayer?.Dispose();
            botChangeLock?.Dispose();

            MultiRunJob.OnCompleted -= UpdateOnCompleted;
            MultiRunJob.OnResult -= UpdateViewModel;
            MultiRunJob.OnStatusChanged -= UpdateStatus;
            MultiRunJob.OnProgress -= UpdateViewModel;

            MultiRunJob.OnResult -= OnResult;
            MultiRunJob.OnResult -= PlayHitSound;
            MultiRunJob.OnTaskError -= OnTaskError;
            MultiRunJob.OnError -= OnError;
            MultiRunJob.OnHit -= OnHit;
        }
        catch
        {
        }
    }
}

#region Other ViewModels
public class BotViewModel(int index, BotData[] datas) : ViewModelBase
{
    private readonly int index = index;
    private readonly BotData[] datas = datas;

    private BotData BotData => datas.Length > index ? datas[index] : null;

    public int Id => index + 1;
    public string Data => BotData?.Line?.Data;
    public string Proxy => BotData?.Proxy?.ToString();
    public string Info => BotData?.ExecutionInfo;

    public override void UpdateViewModel()
    {
        OnPropertyChanged(nameof(Data));
        OnPropertyChanged(nameof(Proxy));
        OnPropertyChanged(nameof(Info));
    }
}

public class HitViewModel(Hit hit) : ViewModelBase
{
    public Hit Hit { get; init; } = hit;

    public DateTime Time => Hit.Date;
    public string Data => Hit.Data.Data;
    public string Proxy => Hit.Proxy?.ToString();
    public string Type => Hit.Type;
    public string Capture => Hit.CapturedDataString;
}
#endregion Other ViewModels

public enum HitsFilter
{
    Hits = 0,
    Custom = 1,
    ToCheck = 2
}


