using IronPython.Hosting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis;
using RuriLib.Helpers.CSharp;
using RuriLib.Helpers.Transpilers;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs;
using RuriLib.Models.Configs.Settings;
using RuriLib.Models.Data;
using RuriLib.Models.Hits;
using RuriLib.Models.Proxies;
using RuriLib.Services;
using RuriLib.Parallelization;
using RuriLib.Parallelization.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using RuriLib.Models.Data.Resources;
using RuriLib.Models.Data.Resources.Options;
using RuriLib.Helpers;
using IronPython.Compiler;
using IronPython.Runtime;
using RuriLib.Models.Captchas;

using RuriLib.Models.Variables;
using RuriLib.Models.Jobs.Execution;
using RuriLib.Models.Jobs.Statistics;
using RuriLib.Models.Jobs.Status;
using RuriLib.Models.Scripting;

namespace RuriLib.Models.Jobs;

public class MultiRunJob : Job, IDisposable
{
    private readonly JobInitializer _initializer = new();
    private readonly JobLifecycleService _lifecycle = new();
    private readonly JobResultProcessor _resultProcessor;

    public MultiRunJob(RuriLibSettingsService settings, PluginRepository pluginRepo, IJobLogger logger = null)
        : base(settings, pluginRepo, logger)
    {
        _resultProcessor = new JobResultProcessor(this);
    }

    /// <summary>
    /// Options
    /// </summary>
    public int Bots { get; set; } = 1;
    public int BotLimit { get; init; } = 500;
    public int Skip { get; set; }
    public Config Config { get; set; }
    public DataPool DataPool { get; set; }
    public List<ProxySource> ProxySources { get; set; } = [];
    public JobProxyMode ProxyMode { get; set; } = JobProxyMode.Default;
    public bool ShuffleProxies { get; set; } = true;
    public NoValidProxyBehaviour NoValidProxyBehaviour { get; set; } = NoValidProxyBehaviour.Reload;
    public TimeSpan ProxyBanTime { get; set; } = TimeSpan.Zero;
    public bool MarkAsToCheckOnAbort { get; set; }
    public bool NeverBanProxies { get; set; }
    public bool ConcurrentProxyMode { get; set; }
    public TimeSpan PeriodicReloadInterval { get; set; } = TimeSpan.Zero;
    public List<IHitOutput> HitOutputs { get; set; } = [];
    public bool CpmTriggerEnabled { get; set; }

    public Bots.Providers Providers { get; set; }
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(1);
    public Dictionary<string, string> CustomInputsAnswers { get; set; } = [];
    public BotData[] CurrentBotDatas { get; set; }

    /// <summary>
    /// Getters
    /// </summary>
    public override float Progress => _parallelizer?.Progress ?? -1;
    public override TimeSpan Elapsed => _parallelizer?.Elapsed ?? TimeSpan.Zero;
    public override TimeSpan Remaining => _parallelizer?.Remaining ?? Timeout.InfiniteTimeSpan;
    public int CPM => _parallelizer?.CPM ?? 0;

    /// <summary>
    /// Private fields
    /// </summary>
    private Parallelizer<MultiRunInput, CheckResult> _parallelizer;
    private JobResourceScope _resourceScope;
    private Timer _tickTimer;
    private Timer _proxyReloadTimer;
    private CancellationTokenSource _startCts;

    // Performance optimizations
    private bool _disposed;
    private int _fatalTaskErrorFlag;

    /// <summary>
    /// Instance properties and stats
    /// </summary>
    public IReadOnlyCollection<Hit> Hits => _resultProcessor.Hits;

    /// <summary>
    /// Events
    /// </summary>
    public event EventHandler<ErrorDetails<MultiRunInput>> OnTaskError;
    public event EventHandler<ResultDetails<MultiRunInput, CheckResult>> OnResult;
    public event EventHandler<Exception> OnError;
    public event EventHandler<float> OnProgress;
    public event EventHandler<JobStatus> OnStatusChanged;
    public event EventHandler OnBotsChanged;
    public event EventHandler OnCompleted;
    public event EventHandler OnTimerTick;
    public event EventHandler<Hit> OnHit;

    /*********
     * STATS *
     *********/

    /// <summary>
    /// Job statistics manager
    /// </summary>
    public JobStatistics Statistics => _resultProcessor.Statistics;

    /// <summary>
    /// -- Data
    /// </summary>
    public int DataTested => Statistics.Tested;
    public int DataHits => Statistics.Hits;
    public int DataCustom => Statistics.Custom;
    public int DataFails => Statistics.Fails;
    public int DataRetried => Statistics.Retried;
    public int DataBanned => Statistics.Banned;
    public int DataToCheck => Statistics.ToCheck;
    public int DataInvalid => Statistics.Invalid;
    public int DataErrors => Statistics.Errors;

    /// <summary>
    /// -- Proxies (cached for performance)
    /// </summary>
    public int ProxiesTotal => _resourceScope?.GetCachedProxyStats().total ?? 0;
    public int ProxiesAlive => _resourceScope?.GetCachedProxyStats().alive ?? 0;
    public int ProxiesBanned => _resourceScope?.GetCachedProxyStats().banned ?? 0;
    public int ProxiesBad => _resourceScope?.GetCachedProxyStats().bad ?? 0;

    /// <summary>
    /// -- Misc
    /// </summary>
    public decimal CaptchaCredit { get; private set; } = 0;

    internal Parallelizer<MultiRunInput, CheckResult> Parallelizer
    {
        get => _parallelizer;
        set => _parallelizer = value;
    }

    internal ProxyPool ProxyPool => _resourceScope?.ProxyPool;

    internal BotExecutionCoordinator ExecutionCoordinator => _resourceScope?.ExecutionCoordinator;

    internal AsyncLocker RuntimeAsyncLocker => _resourceScope?.AsyncLocker;

    internal JobResourceScope ResourceScope
        => _resourceScope ?? throw new InvalidOperationException("Job resources have not been initialized");

    internal JobResultProcessor ResultProcessor => _resultProcessor;

    internal bool ShouldLogAllResults => settings.RuriLibSettings.GeneralSettings.LogAllResults;

    internal bool ShouldPersistBotLogForHits
        => settings.RuriLibSettings.GeneralSettings.EnableBotLogging && Config.Mode != ConfigMode.DLL;

    internal CancellationTokenSource StartCancellationSource => _startCts;

    internal Timer TickTimer
    {
        get => _tickTimer;
        set => _tickTimer = value;
    }

    internal Timer ProxyReloadTimer
    {
        get => _proxyReloadTimer;
        set => _proxyReloadTimer = value;
    }

    #region Work Function

    #endregion Work Function

    #region Controls
    public override async Task Start(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Status is JobStatus.Starting or JobStatus.Running)
        {
            throw new InvalidOperationException("Job already started");
        }

        try
        {
            _startCts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _startCts.Token);

            UpdateStatus(JobStatus.Starting);

            ApplyInitialization(await _initializer
                .InitializeAsync(this, settings, pluginRepo, linkedCts.Token)
                .ConfigureAwait(false));

            await _lifecycle.StartAsync(this, settings, linkedCts.Token).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            throw;
        }
        finally
        {
            if (Status is JobStatus.Starting)
            {
                UpdateStatus(JobStatus.Idle);
            }

            _startCts?.Dispose();
            _startCts = null;
        }
    }

    internal Task WaitForStartConditionAsync(CancellationToken cancellationToken)
        => base.Start(cancellationToken);

    internal void UpdateStatus(JobStatus status)
    {
        Status = status;
        OnStatusChanged?.Invoke(this, Status);
    }

    internal void ApplyInitialization(JobInitializationResult initialization)
    {
        _resourceScope?.Dispose();
        _resourceScope = new JobResourceScope(initialization, ProxySources);
        _disposed = false;
    }

    public override async Task Stop()
        => await _lifecycle.StopAsync(this).ConfigureAwait(false);

    public override async Task Abort()
        => await _lifecycle.AbortAsync(this).ConfigureAwait(false);

    public override async Task Pause()
        => await _lifecycle.PauseAsync(this).ConfigureAwait(false);

    public override async Task Resume()
        => await _lifecycle.ResumeAsync(this).ConfigureAwait(false);
    #endregion Controls

    #region Public Methods
    public async Task FetchProxiesFromSources(CancellationToken cancellationToken = default)
        => await _lifecycle.ReloadProxiesAsync(this, cancellationToken).ConfigureAwait(false);
    #endregion Public Methods

    #region Wrappers for Parallelizer methods
    public async Task ChangeBots(int amount)
    {
        if (_parallelizer is not null)
        {
            await _parallelizer.ChangeDegreeOfParallelism(amount).ConfigureAwait(false);
        }

        Bots = amount;
        logger?.LogInfo(Id, $"Changed bots to {amount}");
        OnBotsChanged?.Invoke(this, EventArgs.Empty);
    }
    #endregion Wrappers for Parallelizer methods

    #region Propagation of Parallelizer events
    internal void HandleTaskError(object _, ErrorDetails<MultiRunInput> details)
    {
        OnTaskError?.Invoke(this, details);
        logger?.LogException(Id, details.Exception);

        if (details.Exception is CompilationErrorException)
        {
            HandleFatalTaskError(details.Exception);
        }
    }

    internal void HandleParallelizerError(object _, Exception ex)
    {
        OnError?.Invoke(this, ex);
        logger?.LogException(Id, ex);
    }

    internal void HandleParallelizerCompleted(object sender, EventArgs e)
    {
        try
        {
            if (Interlocked.CompareExchange(ref _fatalTaskErrorFlag, 0, 0) == 0)
            {
                Skip += DataTested;
            }
            else
            {
                logger?.LogInfo(Id, "Execution aborted due to fatal error. Data pool offset left unchanged.");
            }

            _tickTimer?.Dispose();
            _proxyReloadTimer?.Dispose();
            _tickTimer = null;
            _proxyReloadTimer = null;
            OnCompleted?.Invoke(this, e);
            logger?.LogInfo(Id, "Execution completed");
        }
        finally
        {
            Interlocked.Exchange(ref _fatalTaskErrorFlag, 0);
        }
    }

    private void HandleFatalTaskError(Exception exception)
    {
        if (Interlocked.CompareExchange(ref _fatalTaskErrorFlag, 1, 0) != 0)
        {
            return;
        }

        logger?.LogInfo(Id, "Fatal compilation error detected. Aborting job to prevent data skips.");

        _ = Task.Run(async () =>
        {
            try
            {
                await Abort().ConfigureAwait(false);
            }
            catch (Exception abortEx)
            {
                logger?.LogException(Id, abortEx);
            }
        });
    }
    #endregion Propagation of Parallelizer events

    #region Private Methods
    internal void ResetRuntimeStats()
        => _resultProcessor.Reset();

    internal void HandleParallelizerStatusChanged(object sender, ParallelizerStatus status)
    {
        Status = status switch
        {
            ParallelizerStatus.Idle => JobStatus.Idle,
            ParallelizerStatus.Running => JobStatus.Running,
            ParallelizerStatus.Paused => JobStatus.Paused,
            ParallelizerStatus.Stopping => JobStatus.Stopping,
            _ => throw new NotImplementedException()
        };

        OnStatusChanged?.Invoke(this, Status);
    }

    internal static bool ShouldUseProxies(JobProxyMode mode, ConfigProxySettings settings) => mode switch
    {
        JobProxyMode.Default => settings.UseProxies,
        JobProxyMode.On => true,
        JobProxyMode.Off => false,
        _ => throw new NotImplementedException()
    };

    public void DebugLog(string message)
    {
        if (settings.RuriLibSettings.GeneralSettings.VerboseMode)
        {
            Console.WriteLine($"[{DateTime.Now}] {message}");
        }
    }

    internal void LogInfo(string message)
        => logger?.LogInfo(Id, message);

    internal void RaiseTimerTick()
        => OnTimerTick?.Invoke(this, EventArgs.Empty);

    internal void RaiseError(Exception ex)
        => OnError?.Invoke(this, ex);

    internal void RaiseResult(ResultDetails<MultiRunInput, CheckResult> result)
        => OnResult?.Invoke(this, result);

    internal void RaiseHit(Hit hit)
        => OnHit?.Invoke(this, hit);

    internal void DisposeRuntimeResources()
    {
        _resourceScope?.Dispose();
        _resourceScope = null;
    }

    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            DisposeRuntimeResources();
            _disposed = true;
        }
    }
    #endregion Private Methods
}

public struct MultiRunInput
{
    public MultiRunJob Job { get; set; }
    public BotData BotData { get; set; }
    public dynamic Globals { get; set; }
    public ProxyPool ProxyPool { get; set; }
    public IScript Script { get; set; }
    public bool IsDLL { get; set; }
    public MethodInfo DLLMethod { get; set; }
    public Dictionary<string, string> CustomInputsAnswers { get; set; }
    public long Index { get; set; }
}

public struct CheckResult
{
    public BotData BotData { get; set; }
    public Dictionary<string, object> OutputVariables { get; set; }
}
