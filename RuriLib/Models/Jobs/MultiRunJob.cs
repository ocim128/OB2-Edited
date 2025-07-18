using IronPython.Hosting;
using Microsoft.CodeAnalysis.Scripting;
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
using RuriLib.Legacy.Models;
using RuriLib.Legacy.LS;
using RuriLib.Models.Variables;
using RuriLib.Models.Jobs.Execution;
using RuriLib.Models.Jobs.Statistics;
using RuriLib.Models.Jobs.Status;

namespace RuriLib.Models.Jobs;

public class MultiRunJob(RuriLibSettingsService settings, PluginRepository pluginRepo, IJobLogger logger = null) : Job(settings, pluginRepo, logger), IDisposable
{
    /// <summary>
    /// Options
    /// </summary>
    public int Bots { get; set; } = 1;
    public int BotLimit { get; init; } = 200;
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
    private VariablesList _legacyGlobalVariables;
    private Dictionary<string, string> _legacyGlobalCookies;
    private Dictionary<string, ConfigResource> _resources;
    private HttpClient _httpClient;
    private AsyncLocker _asyncLocker;
    private Timer _proxyReloadTimer;
    private CancellationTokenSource _startCts;
    private MethodInfo _dllMethod;
    private Script _script;

    /// <summary>
    /// Instance properties and stats
    /// </summary>
    public List<Hit> Hits { get; private set; } = [];

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
    /// -- Proxies
    /// </summary>
    public int ProxiesTotal => (_proxyPool?.Proxies.Count()) ?? 0;
    public int ProxiesAlive => (_proxyPool?.Proxies
        .Count(static p => p.ProxyStatus is ProxyStatus.Available or ProxyStatus.Busy)) ?? 0;
    public int ProxiesBanned => (_proxyPool?.Proxies.Count(static p => p.ProxyStatus == ProxyStatus.Banned)) ?? 0;
    public int ProxiesBad => (_proxyPool?.Proxies.Count(static p => p.ProxyStatus == ProxyStatus.Bad)) ?? 0;

    /// <summary>
    /// -- Misc
    /// </summary>
    public decimal CaptchaCredit { get; private set; } = 0;

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

            Status = JobStatus.Starting;
            OnStatusChanged?.Invoke(this, Status);

            _asyncLocker = new();

            await ValidateJobParametersAsync(linkedCts.Token).ConfigureAwait(false);
            await InitializeJobAsync(linkedCts.Token).ConfigureAwait(false);
            await StartExecutionAsync(linkedCts.Token).ConfigureAwait(false);
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
                Status = JobStatus.Idle;
                OnStatusChanged?.Invoke(this, Status);
            }

            _startCts?.Dispose();
            _startCts = null;
        }
    }

    private async Task ValidateJobParametersAsync(CancellationToken cancellationToken)
    {
        if (Config == null)
            throw new ArgumentNullException("config");

        if (DataPool == null)
            throw new ArgumentNullException("dataPool");

        if (Skip >= DataPool.Size)
            throw new ArgumentException(
                "The skip must be smaller than the total number of lines in the data pool");

        if (ShouldUseProxies(ProxyMode, Config.Settings.ProxySettings) &&
            (ProxySources == null || ProxySources.Count == 0))
            throw new ArgumentNullException("proxySources");

        if (!Config.Settings.DataSettings.AllowedWordlistTypes.Contains(DataPool.WordlistType))
            throw new NotSupportedException("This config does not support the provided Wordlist Type");
    }

    private async Task InitializeJobAsync(CancellationToken cancellationToken)
    {
        // Reload the data pool from the source
        DataPool.Reload();

        if (ShouldUseProxies(ProxyMode, Config.Settings.ProxySettings))
        {
            await InitializeProxyPoolAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            InitializeExecutionCoordinatorWithoutProxy();
        }

        await InitializeResourcesAsync(cancellationToken).ConfigureAwait(false);
        await InitializeScriptAsync(cancellationToken).ConfigureAwait(false);
        await InitializeGlobalsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InitializeProxyPoolAsync(CancellationToken cancellationToken)
    {
        // HACK: This should probably not be here, but it will work for now
        ProxySources.ForEach(p => p.UserId = OwnerId);

        var proxyPoolOptions =
            new ProxyPoolOptions { AllowedTypes = Config.Settings.ProxySettings.AllowedProxyTypes };
        _proxyPool = new ProxyPool(ProxySources, proxyPoolOptions);

        try
        {
            await _asyncLocker
                .Acquire(typeof(ProxyPool), nameof(ProxyPool.ReloadAllAsync), cancellationToken)
                .ConfigureAwait(false);
            await _proxyPool.ReloadAllAsync(ShuffleProxies, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _asyncLocker.Release(typeof(ProxyPool), nameof(ProxyPool.ReloadAllAsync));
        }

        var executionHandler = ExecutionHandlerFactory.CreateHandler(Config.Mode);
        _executionCoordinator = new BotExecutionCoordinator(executionHandler, new ProxyManager(this, _asyncLocker));

        if (!_proxyPool.Proxies.Any())
        {
            throw new InvalidOperationException(
                "No proxies that respect the allowed types are available, but the job is set to use proxies");
        }
    }

    private void InitializeExecutionCoordinatorWithoutProxy()
    {
        var executionHandler = ExecutionHandlerFactory.CreateHandler(Config.Mode);
        _executionCoordinator = new BotExecutionCoordinator(executionHandler, null);
    }

    private async Task InitializeResourcesAsync(CancellationToken cancellationToken)
    {
        _resources = [];

        foreach (var opt in Config.Settings.DataSettings.Resources)
        {
            try
            {
                _resources[opt.Name] = opt switch
                {
                    LinesFromFileResourceOptions x => new LinesFromFileResource(x),
                    RandomLinesFromFileResourceOptions x => new RandomLinesFromFileResource(x),
                    _ => throw new NotImplementedException()
                };
            }
            catch
            {
                throw new InvalidOperationException($"Could not create resource {opt.Name}");
            }
        }
    }

    private async Task InitializeScriptAsync(CancellationToken cancellationToken)
    {
        if (Config.Mode is ConfigMode.LoliCode or ConfigMode.Stack)
        {
            Config.StartupCSharpScript =
                Loli2CSharpTranspiler.Transpile(Config.StartupLoliCodeScript, Config.Settings);
        }

        // If not in DLL mode, build the C# script and compile it
        if (Config.Mode == ConfigMode.DLL)
        {
            await using var ms = new MemoryStream(Config.DLLBytes);
            var assembly = AssemblyLoadContext.Default.LoadFromStream(ms);
            var type = assembly.GetType("RuriLib.CompiledConfig");
            _dllMethod = type.GetMember("Execute")[0] as MethodInfo;
        }
        else if (Config.Mode != ConfigMode.Legacy)
        {
            switch (Config.Mode)
            {
                case ConfigMode.Stack:
                    Config.CSharpScript = Stack2CSharpTranspiler.Transpile(Config.Stack, Config.Settings);
                    break;
                case ConfigMode.LoliCode:
                    Config.CSharpScript = Loli2CSharpTranspiler.Transpile(Config.LoliCodeScript, Config.Settings);
                    break;
            }

            _script = new ScriptBuilder().Build(Config.CSharpScript, Config.Settings.ScriptSettings, pluginRepo);
            _ = _script.Compile(cancellationToken);
        }
    }

    private async Task InitializeGlobalsAsync(CancellationToken cancellationToken)
    {
        Providers.Security.X509RevocationMode = Config.Mode == ConfigMode.DLL
            ? System.Security.Cryptography.X509Certificates.X509RevocationMode.Online
            : System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;

        var wordlistType =
            settings.Environment.WordlistTypes.FirstOrDefault(t => t.Name == DataPool.WordlistType);

        _globalVariables = new ExpandoObject();
        _legacyGlobalVariables = new();
        _legacyGlobalCookies = [];
        _httpClient = new();

        var runtime = Python.CreateRuntime();
        var pyengine = runtime.GetEngine("py");
        var pco = (PythonCompilerOptions)pyengine.GetCompilerOptions();
        pco.Module &= ~ModuleOptions.Optimized;

        _globalVariables.Resources = _resources;
        _globalVariables.OwnerId = OwnerId;
        _globalVariables.JobId = Id;

        if (!string.IsNullOrWhiteSpace(Config.StartupCSharpScript))
        {
            await ExecuteStartupScriptAsync(wordlistType, pyengine, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteStartupScriptAsync(dynamic wordlistType, dynamic pyengine, CancellationToken cancellationToken)
    {
        var startupScript = new ScriptBuilder().Build(Config.StartupCSharpScript,
            Config.Settings.ScriptSettings, pluginRepo);
        var startupBotData = new BotData(Providers, Config.Settings, new BotLogger(),
            new DataLine(string.Empty, wordlistType), null, false)
        {
            CancellationToken = cancellationToken
        };
        var startupGlobals = new ScriptGlobals(startupBotData, _globalVariables);
        _ = await startupScript.RunAsync(startupGlobals, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task StartExecutionAsync(CancellationToken cancellationToken)
    {
        var workFunction = new Func<MultiRunInput, CancellationToken, Task<CheckResult>>(async (input, token) =>
        {
            return await _executionCoordinator.ExecuteAsync(input, token);
        });

        Status = JobStatus.Waiting;
        OnStatusChanged?.Invoke(this, Status);

        await base.Start(cancellationToken).ConfigureAwait(false);

        Status = JobStatus.Starting;
        OnStatusChanged?.Invoke(this, Status);

        var wordlistType = settings.Environment.WordlistTypes.FirstOrDefault(t => t.Name == DataPool.WordlistType);
        var workItems = CreateWorkItems(wordlistType);

        _parallelizer = ParallelizerFactory<MultiRunInput, CheckResult>
            .Create(settings.RuriLibSettings.GeneralSettings.ParallelizerType, workItems,
                workFunction, Bots, DataPool.Size, Skip, BotLimit);

        ConfigureParallelizer();

        ResetStats();
        StartTimers();
        logger?.LogInfo(Id, "All set, starting the execution");
        await _parallelizer.Start().ConfigureAwait(false);
    }

    private IEnumerable<MultiRunInput> CreateWorkItems(dynamic wordlistType)
    {
        long index = 0;
        return DataPool.DataList.Select(line => new MultiRunInput
        {
            Job = this,
            ProxyPool = _proxyPool,
            BotData = new BotData(Providers, Config.Settings, new BotLogger(),
                new DataLine(line, wordlistType),
                null, ShouldUseProxies(ProxyMode, Config.Settings.ProxySettings)),
            Globals = _globalVariables,
            LegacyLoliScript = Config.LoliScript,
            LegacyGlobals = _legacyGlobalVariables,
            LegacyGlobalCookies = _legacyGlobalCookies,
            Script = _script,
            IsDLL = Config.Mode == ConfigMode.DLL,
            IsLegacy = Config.Mode == ConfigMode.Legacy,
            DLLMethod = _dllMethod,
            CustomInputsAnswers = CustomInputsAnswers,
            Index = index++
        });
    }

    private void ConfigureParallelizer()
    {
        _parallelizer.CPMLimit = Config.Settings.GeneralSettings.MaximumCPM;
        _parallelizer.NewResult += DataProcessed;
        _parallelizer.StatusChanged += StatusChanged;
        _parallelizer.TaskError += PropagateTaskError;
        _parallelizer.Error += PropagateError;
        _parallelizer.NewResult += PropagateResult;
        _parallelizer.Completed += PropagateCompleted;
        _parallelizer.Completed += (s, e) => Skip += DataTested;
    }

    public override async Task Stop()
    {
        try
        {
            if (_parallelizer is not null)
            {
                await _parallelizer.Stop().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            throw;
        }
        finally
        {
            StopTimers();
            logger?.LogInfo(Id, "Execution stopped");
            DisposeGlobals();
        }
    }

    public override async Task Abort()
    {
        try
        {
            if (_parallelizer is not null)
            {
                await _parallelizer.Abort().ConfigureAwait(false);
            }

            if (_startCts is not null)
            {
                await _startCts.CancelAsync();
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            throw;
        }
        finally
        {
            StopTimers();
            logger?.LogInfo(Id, "Execution aborted");
            DisposeGlobals();
        }
    }

    public override async Task Pause()
    {
        try
        {
            if (_parallelizer is not null)
            {
                await _parallelizer.Pause().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            throw;
        }
        finally
        {
            StopTimers();
            logger?.LogInfo(Id, "Execution paused");
        }
    }

    public override async Task Resume()
    {
        try
        {
            if (_parallelizer is not null)
            {
                await _parallelizer.Resume().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            throw;
        }

        StartTimers();
        logger?.LogInfo(Id, "Execution resumed");
    }
    #endregion Controls

    #region Public Methods
    public async Task FetchProxiesFromSources(CancellationToken cancellationToken = default)
    {
        try
        {
            await _asyncLocker.Acquire(typeof(ProxyPool), nameof(ProxyPool.ReloadAllAsync), cancellationToken).ConfigureAwait(false);
            await _proxyPool.ReloadAllAsync(ShuffleProxies, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _asyncLocker.Release(typeof(ProxyPool), nameof(ProxyPool.ReloadAllAsync));
        }
    }
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
    private void PropagateTaskError(object _, ErrorDetails<MultiRunInput> details)
    {
        OnTaskError?.Invoke(this, details);
        logger?.LogException(Id, details.Exception);
    }

    private void PropagateError(object _, Exception ex)
    {
        OnError?.Invoke(this, ex);
        logger?.LogException(Id, ex);
    }

    private void PropagateResult(object _, ResultDetails<MultiRunInput, CheckResult> result)
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
    #endregion Propagation of Parallelizer events

    #region Private Methods
    private void StartTimers()
    {
        _tickTimer = new Timer(new TimerCallback(_ => OnTimerTick?.Invoke(this, EventArgs.Empty)),
            null, (int)TickInterval.TotalMilliseconds, (int)TickInterval.TotalMilliseconds);

        if (PeriodicReloadInterval > TimeSpan.Zero)
        {
            _proxyReloadTimer = new Timer(new TimerCallback(async _ =>
            {
                if (_proxyPool is not null)
                {
                    try
                    {
                        await _asyncLocker.Acquire(typeof(ProxyPool), nameof(ProxyPool.ReloadAllAsync), CancellationToken.None)
                            .ConfigureAwait(false);
                        await _proxyPool.ReloadAllAsync(ShuffleProxies).ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignored
                    }
                    finally
                    {
                        _asyncLocker.Release(typeof(ProxyPool), nameof(ProxyPool.ReloadAllAsync));
                    }
                }
            }), null, (int)PeriodicReloadInterval.TotalMilliseconds, (int)PeriodicReloadInterval.TotalMilliseconds);
        }
    }

    private void StopTimers()
    {
        _tickTimer?.Dispose();
        _proxyReloadTimer?.Dispose();
    }

    private void ResetStats()
    {
        Statistics.Reset();
        Hits = [];
    }

    private void StatusChanged(object sender, ParallelizerStatus status)
    {
        Status = status switch
        {
            ParallelizerStatus.Idle => JobStatus.Idle,
            ParallelizerStatus.Starting => JobStatus.Starting,
            ParallelizerStatus.Running => JobStatus.Running,
            ParallelizerStatus.Pausing => JobStatus.Pausing,
            ParallelizerStatus.Paused => JobStatus.Paused,
            ParallelizerStatus.Stopping => JobStatus.Stopping,
            ParallelizerStatus.Resuming => JobStatus.Resuming,
            _ => throw new NotImplementedException()
        };

        OnStatusChanged?.Invoke(this, Status);
    }

    private void DataProcessed(object sender, ResultDetails<MultiRunInput, CheckResult> details)
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

        Hits.Add(hit);
        OnHit?.Invoke(this, hit);

        foreach (var hitOutput in HitOutputs)
        {
            await hitOutput.Store(hit).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, object> CleanEmptyCaptures(Dictionary<string, object> capturedData)
    {
        var newCaptures = new Dictionary<string, object>();

        foreach (var item in capturedData)
        {
            if (item.Value is string stringValue && string.IsNullOrWhiteSpace(stringValue))
                continue;

            if (item.Value is byte[] bytesValue && bytesValue.Length == 0)
                continue;

            if (item.Value is List<string> listValue && listValue.Count == 0)
                continue;

            if (item.Value is Dictionary<string, string> dictValue && dictValue.Count == 0)
                continue;

            newCaptures.Add(item.Key, item.Value);
        }

        return newCaptures;
    }

    private static bool ShouldUseProxies(JobProxyMode mode, ProxySettings settings) => mode switch
    {
        JobProxyMode.Default => settings.UseProxies,
        JobProxyMode.On => true,
        JobProxyMode.Off => false,
        _ => throw new NotImplementedException()
    };

    public void DebugLog(string message)
    {
        if (Providers.GeneralSettings.VerboseMode)
        {
            Console.WriteLine($"[{DateTime.Now}] {message}");
        }
    }

    private void DisposeGlobals()
    {
        _httpClient?.Dispose();
        _asyncLocker?.Dispose();
        _proxyPool?.Dispose();

        if (ProxySources is not null)
        {
            for (var i = 0; i < ProxySources.Count; i++)
            {
                ProxySources[i]?.Dispose();
            }
        }

        if (_resources is not null)
        {
            foreach (var resource in _resources.Where(static r => r.Value is IDisposable)
                .Select(static r => r.Value).Cast<IDisposable>())
            {
                try
                {
                    resource.Dispose();
                }
                catch
                {
                }
            }
        }

        _executionCoordinator = null;
    }

    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // dispose managed resources
        }
        // free unmanaged resources
    }
    #endregion Private Methods
}

public struct MultiRunInput
{
    public MultiRunJob Job { get; set; }
    public BotData BotData { get; set; }
    public dynamic Globals { get; set; }
    public ProxyPool ProxyPool { get; set; }
    public Script Script { get; set; }
    public bool IsDLL { get; set; }
    public bool IsLegacy { get; set; }
    public string LegacyLoliScript { get; set; }
    public VariablesList LegacyGlobals { get; set; }
    public Dictionary<string, string> LegacyGlobalCookies { get; set; }
    public MethodInfo DLLMethod { get; set; }
    public Dictionary<string, string> CustomInputsAnswers { get; set; }
    public long Index { get; set; }
}

public struct CheckResult
{
    public BotData BotData { get; set; }
    public Dictionary<string, object> OutputVariables { get; set; }
}
