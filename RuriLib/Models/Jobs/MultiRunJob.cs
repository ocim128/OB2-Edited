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

public class MultiRunJob(RuriLibSettingsService settings, PluginRepository pluginRepo, IJobLogger logger = null) : Job(settings, pluginRepo, logger), IDisposable
{
    private readonly JobInitializer _initializer = new();
    private readonly JobLifecycleService _lifecycle = new();

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
    private ProxyPool _proxyPool;

    private BotExecutionCoordinator _executionCoordinator;
    private Timer _tickTimer;
    private dynamic _globalVariables;
    private Dictionary<string, ConfigResource> _resources;
    private HttpClient _httpClient;
    private AsyncLocker _asyncLocker;
    private Timer _proxyReloadTimer;
    private CancellationTokenSource _startCts;
    private MethodInfo _dllMethod;
    private IScript _script;

    // Performance optimizations
    private static readonly char[] _separator = ['\r', '\n'];
    private readonly object _lockObject = new();
    private readonly ConcurrentQueue<Hit> hits = new();
    private bool _disposed;
    private int _fatalTaskErrorFlag;

    // Lazy initialization for expensive resources
    private Lazy<dynamic> _pythonEngine;
    private DateTime _lastProxyStatsUpdate = DateTime.MinValue;
    private (int total, int alive, int banned, int bad) _cachedProxyStats;
    private readonly TimeSpan _proxyStatsCacheInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Instance properties and stats
    /// </summary>
    public IReadOnlyCollection<Hit> Hits => hits;

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
    public JobStatistics Statistics { get; private set; } = new();

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
    public int ProxiesTotal => GetCachedProxyStats().total;
    public int ProxiesAlive => GetCachedProxyStats().alive;
    public int ProxiesBanned => GetCachedProxyStats().banned;
    public int ProxiesBad => GetCachedProxyStats().bad;

    /// <summary>
    /// -- Misc
    /// </summary>
    public decimal CaptchaCredit { get; private set; } = 0;

    internal Parallelizer<MultiRunInput, CheckResult> Parallelizer
    {
        get => _parallelizer;
        set => _parallelizer = value;
    }

    internal ProxyPool ProxyPool => _proxyPool;

    internal BotExecutionCoordinator ExecutionCoordinator => _executionCoordinator;

    internal AsyncLocker RuntimeAsyncLocker => _asyncLocker;

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
        _asyncLocker = initialization.AsyncLocker;
        _proxyPool = initialization.ProxyPool;
        _executionCoordinator = initialization.ExecutionCoordinator;
        _resources = initialization.Resources;
        _httpClient = initialization.HttpClient;
        _pythonEngine = initialization.PythonEngine;
        _globalVariables = initialization.GlobalVariables;
        _dllMethod = initialization.DllMethod;
        _script = initialization.Script;
        _disposed = false;
    }

    internal IEnumerable<MultiRunInput> CreateWorkItems(dynamic wordlistType)
    {
        // Cache frequently accessed values to reduce property lookups
        var useProxies = ShouldUseProxies(ProxyMode, Config.Settings.ProxySettings);
        var configMode = Config.Mode;
        var isDll = configMode == ConfigMode.DLL;
        var configSettings = Config.Settings;
        var customAnswers = CustomInputsAnswers;

        long index = 0;

        // Use yield return to avoid large array allocation
        foreach (var line in DataPool.DataList)
        {
            yield return new MultiRunInput
            {
                Job = this,
                ProxyPool = _proxyPool,
                BotData = new BotData(Providers, configSettings, new BotLogger(),
                    new DataLine(line, wordlistType), null, useProxies),
                Globals = _globalVariables,
                Script = _script,
                IsDLL = isDll,
                DLLMethod = _dllMethod,
                CustomInputsAnswers = customAnswers,
                Index = index++
            };
        }
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

    internal void HandleParallelizerResult(object _, ResultDetails<MultiRunInput, CheckResult> result)
    {
        OnResult?.Invoke(this, result);

        if (!settings.RuriLibSettings.GeneralSettings.LogAllResults)
        {
            return;
        }

        var data = result.Result.BotData;
        logger?.LogInfo(Id, $"[{data.STATUS}] {data.Line.Data} ({data.Proxy})");
    }

    private void PropagateCompleted(object _, EventArgs e)
    {
        OnCompleted?.Invoke(this, e);
        logger?.LogInfo(Id, "Execution completed");
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
            PropagateCompleted(sender, e);
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
    {
        Statistics.Reset();
        hits.Clear();
    }

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

    internal void HandleDataProcessed(object sender, ResultDetails<MultiRunInput, CheckResult> details)
    {
        var botData = details.Result.BotData;

        if (BotStatus.IsHitStatus(botData.STATUS))
        {
            _ = RegisterHit(details.Result).ConfigureAwait(false);
        }

        switch (botData.STATUS)
        {
            case "SUCCESS":
                Statistics.IncrementHits();
                break;
            case "NONE":
                Statistics.IncrementToCheck();
                break;
            case "FAIL":
                Statistics.IncrementFails();
                break;
            case "INVALID":
                Statistics.IncrementInvalid();
                break;
            case "ERROR":
                Statistics.IncrementErrors();
                break;
            case "RETRY":
                Statistics.IncrementRetried();
                break;
            case "BAN":
                Statistics.IncrementBanned();
                break;
            default:
                Statistics.IncrementCustom();
                break;
        }
        Statistics.IncrementTested();

        if (_parallelizer.Status == ParallelizerStatus.Stopping)
        {
            details.Item.BotData.ExecutionInfo = "STOPPED";
        }
    }

    private async Task RegisterHit(CheckResult result)
    {
        var botData = result.BotData;

        var hit = new Hit()
        {
            Data = botData.Line,
            BotLogger = settings.RuriLibSettings.GeneralSettings.EnableBotLogging && Config.Mode != ConfigMode.DLL
                ? botData.Logger
                : null,
            Type = botData.STATUS,
            DataPool = DataPool,
            Config = Config,
            Date = DateTime.Now,
            Proxy = botData.Proxy,
            CapturedData = Config.Settings.GeneralSettings.SaveEmptyCaptures
                ? result.OutputVariables : CleanEmptyCaptures(result.OutputVariables),
            OwnerId = OwnerId
        };

        hits.Enqueue(hit);
        OnHit?.Invoke(this, hit);

        foreach (var hitOutput in HitOutputs)
        {
            await hitOutput.Store(hit).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, object> CleanEmptyCaptures(Dictionary<string, object> capturedData)
    {
        var newCaptures = new Dictionary<string, object>(capturedData.Count);

        foreach (var kvp in capturedData)
        {
            var value = kvp.Value;
            if (value is string s && string.IsNullOrWhiteSpace(s)) continue;
            if (value is byte[] b && b.Length == 0) continue;
            if (value is List<string> l && l.Count == 0) continue;
            if (value is Dictionary<string, string> d && d.Count == 0) continue;

            newCaptures[kvp.Key] = value;
        }

        return newCaptures;
    }

    internal static bool ShouldUseProxies(JobProxyMode mode, ProxySettings settings) => mode switch
    {
        JobProxyMode.Default => settings.UseProxies,
        JobProxyMode.On => true,
        JobProxyMode.Off => false,
        _ => throw new NotImplementedException()
    };

    private (int total, int alive, int banned, int bad) GetCachedProxyStats()
    {
        var now = DateTime.UtcNow;
        if (now - _lastProxyStatsUpdate < _proxyStatsCacheInterval)
        {
            return _cachedProxyStats;
        }

        if (_proxyPool?.Proxies == null)
        {
            _cachedProxyStats = (0, 0, 0, 0);
        }
        else
        {
            var proxies = _proxyPool.Proxies;
            var total = 0;
            var alive = 0;
            var banned = 0;
            var bad = 0;

            foreach (var proxy in proxies)
            {
                total++;
                switch (proxy.ProxyStatus)
                {
                    case ProxyStatus.Available:
                    case ProxyStatus.Busy:
                        alive++;
                        break;
                    case ProxyStatus.Banned:
                        banned++;
                        break;
                    case ProxyStatus.Bad:
                        bad++;
                        break;
                }
            }

            _cachedProxyStats = (total, alive, banned, bad);
        }

        _lastProxyStatsUpdate = now;
        return _cachedProxyStats;
    }

    public void DebugLog(string message)
    {
        if (Providers.GeneralSettings.VerboseMode)
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

    internal void DisposeRuntimeResources()
        => DisposeGlobals();

    private void DisposeGlobals()
    {
        if (_disposed) return;

        _httpClient?.Dispose();
        _asyncLocker?.Dispose();
        _proxyPool?.Dispose();

        if (ProxySources is not null)
        {
            for (var i = 0; i < ProxySources.Count; i++)
            {
                try
                {
                    ProxySources[i]?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }
        }

        if (_resources is not null)
        {
            foreach (var resource in _resources.Values)
            {
                if (resource is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }
            }
        }

        _executionCoordinator = null;
        _disposed = true;
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
            DisposeGlobals();
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
