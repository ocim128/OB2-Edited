using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flux.Core.Services;
using Flux.Native.Helpers;
using Flux.Native.Utils;
using Flux.Native.ViewModels.Base;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;
using RuriLib.Models.Jobs;

namespace Flux.Native.ViewModels.Jobs;

public partial class MultiRunJobViewerViewModel : ViewModelBase, IDisposable
{
    private readonly FluxSettingsService fluxSettingsService;
    private readonly IJobCommands jobCommands;
    private readonly IJobQueries jobQueries;
    private Timer refreshTimer;
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

    private MultiRunJobViewerViewModel(
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
    }

    /// <summary>
    /// Creates and asynchronously initializes a new MultiRunJobViewerViewModel.
    /// Use this instead of the constructor to avoid blocking the UI thread.
    /// </summary>
    public static async Task<MultiRunJobViewerViewModel> CreateAsync(
        MultiRunJobViewModel jobVM,
        FluxSettingsService fluxSettingsService,
        IJobCommands jobCommands,
        IJobQueries jobQueries)
    {
        var vm = new MultiRunJobViewerViewModel(jobVM, fluxSettingsService, jobCommands, jobQueries);

        vm.snapshot = await jobQueries.GetMultiRunJobViewerSnapshotAsync(jobVM.Id).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Multi-run job {jobVM.Id} could not be loaded");

        vm.ApplySnapshot(vm.snapshot, refreshResults: true);
        vm.refreshTimer = new Timer(_ => _ = vm.RefreshAsync(), null, 1000, 1000);
        return vm;
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

    public Task<BotLogDto?> GetBotLogAsync(string resultId)
        => jobQueries.GetBotLogAsync(Job.Id, resultId);

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
}
