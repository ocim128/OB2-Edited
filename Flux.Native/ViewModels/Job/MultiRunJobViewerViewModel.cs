using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flux.Core.Services;
using Flux.Native.Helpers;
using Flux.Native.Utils;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;
using Flux.Native.ViewModels.Base;
using RuriLib.Models.Configs;
using RuriLib.Models.Jobs;

namespace Flux.Native.ViewModels.Jobs;

public class MultiRunJobViewerViewModel : ViewModelBase, IDisposable
{
    private readonly FluxSettingsService fluxSettingsService;
    private readonly IJobCommands jobCommands;
    private readonly IJobQueries jobQueries;
    private readonly Timer refreshTimer;
    private readonly SoundPlayer soundPlayer;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly SemaphoreSlim botChangeLock = new(1, 1);

    private MultiRunJobViewerSnapshotDto snapshot;
    private IReadOnlyList<JobRuntimeResultDto> allResults = [];
    private Dictionary<string, string> customInputAnswers = [];
    private string lastConfigIconBase64 = string.Empty;

    private const int MaxHistoryPoints = 30;
    private readonly List<double> cpmHistory = new();
    private readonly List<double> hitsPerMinuteHistory = new();
    private int lastRecordedHits;
    private DateTime lastHitsRecordTime = DateTime.Now;
    private int lastSoundHitsCount;

    public event Action<object, string, Color> NewMessage;
    public event Action SparklineDataUpdated;

    public MultiRunJobViewModel Job { get; }

    public BitmapImage ConfigIcon { get; private set; }

    public string ConfigNameAndAuthor => $"{ConfigName} {ConfigAuthor}".Trim();
    public string ConfigName => snapshot?.ConfigName ?? "No config";
    public string ConfigAuthor => snapshot?.ConfigAuthor ?? string.Empty;
    public string DataPoolInfo => snapshot?.DataPoolInfo ?? "Unknown";
    public string ProxySourcesInfo => snapshot?.ProxySourcesInfo ?? "None";
    public string HitOutputsInfo => snapshot?.HitOutputsInfo ?? "None";
    public string CustomInputsInfo => string.Join(", ", customInputAnswers.Select(static kvp => $"{kvp.Key}: {kvp.Value}"));
    public bool HasCustomInputs => snapshot?.CustomInputs.Count > 0;
    public bool EnableJobLog => fluxSettingsService.Settings.GeneralSettings.EnableJobLogging;
    public string RemainingWaitString => !IsWaiting || snapshot?.WaitUntil is null
        ? string.Empty
        : (snapshot.WaitUntil.Value - DateTime.Now).ToString(@"hh\:mm\:ss");

    public bool IsWaiting => Job.Status is JobStatus.Waiting;
    public bool CanChangeOptions => Job.Status is JobStatus.Idle;
    public bool CanStart => Job.Status is JobStatus.Idle;
    public bool CanSkipWait => Job.Status is JobStatus.Waiting;
    public bool CanPause => Job.Status is JobStatus.Running;
    public bool CanResume => Job.Status is JobStatus.Paused;
    public bool CanStop => Job.Status is JobStatus.Running or JobStatus.Paused;
    public bool CanAbort => Job.Status is JobStatus.Starting or JobStatus.Running or JobStatus.Paused or JobStatus.Pausing or JobStatus.Stopping;
    public bool IsStarting => Job.Status is JobStatus.Starting;
    public bool IsStopping => Job.Status is JobStatus.Stopping;
    public bool IsPausing => Job.Status is JobStatus.Pausing;
    public double Progress => Math.Clamp(Job.Progress * 100, 0, 100);

    public int HitsCount => snapshot?.Summary.DataHits ?? 0;
    public int CustomCount => snapshot?.Summary.DataCustom ?? 0;
    public int ToCheckCount => snapshot?.DataToCheck ?? 0;

    public string HitsTabLabel => HitsCount > 0 ? $"Hits ({HitsCount})" : "Hits";
    public string CustomTabLabel => CustomCount > 0 ? $"Custom ({CustomCount})" : "Custom";
    public string ToCheckTabLabel => ToCheckCount > 0 ? $"ToCheck ({ToCheckCount})" : "ToCheck";

    public IReadOnlyList<double> CpmHistory => cpmHistory;
    public IReadOnlyList<double> HitsPerMinuteHistory => hitsPerMinuteHistory;
    public double AnimatedCpm => Job.CPM;
    public double AnimatedHits => snapshot?.Summary.DataHits ?? 0;
    public double AnimatedCustom => snapshot?.Summary.DataCustom ?? 0;
    public double AnimatedToCheck => snapshot?.DataToCheck ?? 0;
    public double AnimatedBanned => snapshot?.DataBanned ?? 0;
    public double AnimatedFails => snapshot?.DataFails ?? 0;
    public double AnimatedRetried => snapshot?.DataRetried ?? 0;
    public double AnimatedErrors => snapshot?.DataErrors ?? 0;

    private ObservableCollection<BotViewModel> botsCollection = [];
    public ObservableCollection<BotViewModel> BotsCollection
    {
        get => botsCollection;
        private set
        {
            botsCollection = value;
            OnPropertyChanged();
        }
    }

    private ObservableCollection<HitViewModel> hitsCollection = [];
    public ObservableCollection<HitViewModel> HitsCollection
    {
        get => hitsCollection;
        private set
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

    public MultiRunJobViewerViewModel(
        MultiRunJobViewModel jobVM,
        FluxSettingsService fluxSettingsService,
        IJobCommands jobCommands,
        IJobQueries jobQueries)
    {
        Job = jobVM;
        this.fluxSettingsService = fluxSettingsService;
        this.jobCommands = jobCommands;
        this.jobQueries = jobQueries;
        soundPlayer = new SoundPlayer("Sounds/hit.wav");

        snapshot = jobQueries.GetMultiRunJobViewerSnapshotAsync(jobVM.Id).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException($"Multi-run job {jobVM.Id} could not be loaded");

        ApplySnapshot(snapshot, refreshResults: true);
        refreshTimer = new Timer(_ => _ = RefreshAsync(), null, 1000, 1000);
    }

    public void ClearSparklineData()
    {
        cpmHistory.Clear();
        hitsPerMinuteHistory.Clear();
        lastRecordedHits = 0;
        lastHitsRecordTime = DateTime.Now;
        SparklineDataUpdated?.Invoke();
    }

    public async Task StartAsync()
    {
        if (!(snapshot?.HasConfig ?? false))
        {
            Alert.Error(
                "Config missing",
                "This job references a config that is no longer available. Edit the job to select a valid config before starting.");
            return;
        }

        try
        {
            HitsCollection = [];
            ClearSparklineData();
            customInputAnswers = AskCustomInputs();
            OnPropertyChanged(nameof(CustomInputsInfo));
            await jobCommands.StartAsync(Job.Id, customInputAnswers).ConfigureAwait(false);
            await RefreshAsync(forceResultsRefresh: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public Task StopAsync() => jobCommands.StopAsync(Job.Id);

    public Task AbortAsync() => jobCommands.AbortAsync(Job.Id);

    public Task PauseAsync() => jobCommands.PauseAsync(Job.Id);

    public Task ResumeAsync() => jobCommands.ResumeAsync(Job.Id);

    public Task SkipWaitAsync() => jobCommands.SkipWaitAsync(Job.Id);

    public async Task ChangeBotsAsync(int newValue)
    {
        await jobCommands.ChangeBotsAsync(Job.Id, newValue).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    public async Task IncreaseBotsByAsync(int amount)
    {
        if (!await botChangeLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var newValue = Math.Max(1, Job.Bots + amount);
            await ChangeBotsAsync(newValue).ConfigureAwait(false);
        }
        finally
        {
            botChangeLock.Release();
        }
    }

    public async Task DecreaseBotsByAsync(int amount)
    {
        if (!await botChangeLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var newValue = Math.Max(1, Job.Bots - amount);
            await ChangeBotsAsync(newValue).ConfigureAwait(false);
        }
        finally
        {
            botChangeLock.Release();
        }
    }

    public async Task ResetSkipAsync()
    {
        await jobCommands.ResetSkipAsync(Job.Id).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    public string GetAllHitsForClipboard()
        => string.Join(Environment.NewLine, allResults
            .Where(static hit => hit.Type == "SUCCESS")
            .Select(static hit => hit.Data ?? string.Empty));

    public string GetAllHitsWithCaptureForClipboard()
        => string.Join(Environment.NewLine, allResults
            .Where(static hit => hit.Type == "SUCCESS")
            .Select(static hit => $"{hit.Data} | {hit.Capture}"));

    public Task<BotLogDto?> GetBotLogAsync(string resultId)
        => jobQueries.GetBotLogAsync(Job.Id, resultId);

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
        var botItems = bots.Select(static bot => new BotViewModel(bot));
        RunOnUiThread(() => BotsCollection = new ObservableCollection<BotViewModel>(botItems));
    }

    private void UpdateHitsCollection()
    {
        var filteredHits = ApplySearchFilter(allResults)
            .Where(MatchesFilter)
            .Select(static hit => new HitViewModel(hit))
            .ToList();

        RunOnUiThread(() => HitsCollection = new ObservableCollection<HitViewModel>(filteredHits));
    }

    private IEnumerable<JobRuntimeResultDto> ApplySearchFilter(IEnumerable<JobRuntimeResultDto> hits)
    {
        var query = SearchQuery;
        if (string.IsNullOrWhiteSpace(query))
        {
            return hits;
        }

        return hits.Where(hit => HitMatchesSearch(hit, query));
    }

    private bool MatchesFilter(JobRuntimeResultDto hit)
        => HitsFilter switch
        {
            HitsFilter.Hits => hit.Type == "SUCCESS",
            HitsFilter.ToCheck => hit.Type == "NONE",
            HitsFilter.Custom => hit.Type is not "SUCCESS" and not "NONE" and not "FAIL",
            _ => false
        };

    private static bool HitMatchesSearch(JobRuntimeResultDto hit, string query)
        => ContainsIgnoreCase(hit.Data, query)
            || ContainsIgnoreCase(hit.Proxy, query)
            || ContainsIgnoreCase(hit.Type, query)
            || ContainsIgnoreCase(hit.Capture, query);

    private static bool ContainsIgnoreCase(string source, string query)
        => !string.IsNullOrEmpty(source) && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private Dictionary<string, string> AskCustomInputs()
    {
        var answers = new Dictionary<string, string>();
        if (snapshot?.CustomInputs is null || snapshot.CustomInputs.Count == 0)
        {
            return answers;
        }

        foreach (var input in snapshot.CustomInputs)
        {
            answers[input.VariableName] = Alert.CustomInput(input.Description, input.DefaultAnswer);
        }

        return answers;
    }

    private void RecordSparklineData()
    {
        cpmHistory.Add(Job.CPM);
        while (cpmHistory.Count > MaxHistoryPoints)
        {
            cpmHistory.RemoveAt(0);
        }

        var now = DateTime.Now;
        var elapsedMinutes = (now - lastHitsRecordTime).TotalMinutes;
        if (elapsedMinutes > 0)
        {
            var currentHits = HitsCount;
            var hitsDelta = currentHits - lastRecordedHits;
            hitsPerMinuteHistory.Add(Math.Max(0, hitsDelta / elapsedMinutes));
            while (hitsPerMinuteHistory.Count > MaxHistoryPoints)
            {
                hitsPerMinuteHistory.RemoveAt(0);
            }

            lastRecordedHits = currentHits;
            lastHitsRecordTime = now;
        }

        SparklineDataUpdated?.Invoke();
    }

    private void TryPlayHitSound()
    {
        try
        {
            soundPlayer.Play();
        }
        catch
        {
        }
    }

    private static void RunOnUiThread(Action action)
    {
        if (Application.Current?.Dispatcher is null || Application.Current.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Application.Current.Dispatcher.Invoke(action);
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

public class HitViewModel : ViewModelBase
{
    public HitViewModel(JobRuntimeResultDto hit)
    {
        Hit = hit;
    }

    public JobRuntimeResultDto Hit { get; }
    public string ResultId => Hit.Id;
    public DateTime Time => Hit.Timestamp;
    public string Data => Hit.Data;
    public string Proxy => Hit.Proxy;
    public string Type => Hit.Type;
    public string Capture => Hit.Capture;
    public RuriLib.Models.Proxies.ProxyType? ProxyType => Hit.ProxyType;
    public ConfigMode? ConfigMode => Hit.ConfigMode;
    public bool HasBotLog => Hit.HasBotLog;
}

public enum HitsFilter
{
    Hits = 0,
    Custom = 1,
    ToCheck = 2
}
