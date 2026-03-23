using IronPython.Compiler;
using IronPython.Hosting;
using IronPython.Runtime;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;
using PuppeteerSharp;
using RuriLib.Exceptions;
using RuriLib.Helpers;
using RuriLib.Helpers.Blocks;
using RuriLib.Helpers.CSharp;
using RuriLib.Helpers.Transpilers;

using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs;
using RuriLib.Models.Data;
using RuriLib.Models.Data.Resources;
using RuriLib.Models.Data.Resources.Options;
using RuriLib.Models.Proxies;
using RuriLib.Models.Variables;
using RuriLib.Providers.RandomNumbers;
using RuriLib.Providers.UserAgents;
using RuriLib.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using RuriLib.Models.Scripting;

namespace RuriLib.Models.Debugger;

public enum ConfigDebuggerStatus
{
    Idle = 0,
    Running = 1,
    WaitingForStep = 2
}

public partial class ConfigDebugger : IDisposable
{
    private sealed class CachedTranspilation
    {
        public string MainScript { get; init; } = string.Empty;
        public string StartupScript { get; init; } = string.Empty;
        public DateTime LastAccessedUtc { get; set; } = DateTime.UtcNow;
    }

    private static readonly ConcurrentDictionary<string, CachedTranspilation> _transpilationCache = new();
    private static readonly ScriptPreparationService _scriptPreparation = new();
    private const int MaxTranspilationCacheSize = 40;

    public IRandomUAProvider RandomUAProvider { get; set; }
    public IRNGProvider RNGProvider { get; set; }
    public RuriLibSettingsService RuriLibSettings { get; set; }
    public PluginRepository PluginRepo { get; set; }

    public ConfigDebuggerStatus Status { get; private set; }

    public Config Config { get; init; }
    public DebuggerOptions Options { get; init; }
    public BotLogger Logger { get; init; }

    public event EventHandler<ConfigDebuggerStatus> StatusChanged;
    public event EventHandler<BotLoggerEntry> NewLogEntry;

    private BotData _data;
    private Stepper _stepper;
    private CancellationTokenSource _cts;
    private Browser _lastPuppeteerBrowser;
    private OpenQA.Selenium.WebDriver _lastSeleniumBrowser;
    private Microsoft.Playwright.IBrowser _lastPlaywrightBrowser;
    private Microsoft.Playwright.IPlaywright _lastPlaywrightInstance;

    // Performance optimization: Cache frequently used objects
    private readonly object _statusLock = new();
    private readonly StringBuilder _logBuilder = new(1024); // Reusable StringBuilder
    private readonly BotSessionFactory _botSessionFactory = new();

    public List<Variable> Variables { get; } = new();

    public ConfigDebugger(Config config, DebuggerOptions options = null, BotLogger logger = null)
    {
        Config = config;
        Options = options ?? new DebuggerOptions();
        Logger = logger ?? new BotLogger();
        Logger.NewEntry += OnNewEntry;
    }

    public static Task PrewarmAsync(Config config, PluginRepository pluginRepo, bool stepByStep = false, CancellationToken cancellationToken = default)
    {
        if (config is null)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = PrepareScripts(config, stepByStep, pluginRepo);
        }, cancellationToken);
    }

    public async Task Run()
    {
        // Offload CPU-intensive transpilation and compilation to a background thread
        // This prevents the UI thread from hanging during initialization
        var prepareSw = Stopwatch.StartNew();
        var preparedScripts = await Task.Run(
            () => PrepareScripts(Config, Options.StepByStep, PluginRepo)).ConfigureAwait(false);
        prepareSw.Stop();

        var script = preparedScripts.Script;
        var startupScript = preparedScripts.StartupScript;
        Config.CSharpScript = preparedScripts.TranspiledScript;
        Config.StartupCSharpScript = preparedScripts.TranspiledStartupScript;

        if (Options.UseProxy && !Options.TestProxy.Contains(':'))
        {
            throw new InvalidProxyException(Options.TestProxy);
        }

        if (!Options.PersistLog)
        {
            Logger.Clear();
        }

        if (prepareSw.ElapsedMilliseconds > 0)
        {
            Logger.Log($"SCRIPT PREPARED IN {prepareSw.ElapsedMilliseconds} ms", LogColors.Gray);
        }


        Variables.Clear();
        ChangeStatus(ConfigDebuggerStatus.Running);
        _cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();

        var wordlistType = RuriLibSettings.Environment.WordlistTypes.FirstOrDefault(w => w.Name == Options.WordlistType);
        if (wordlistType == null)
        {
            throw new ArgumentException($"Wordlist type '{Options.WordlistType}' not found");
        }
        var dataLine = new DataLine(Options.TestData, wordlistType);
        var proxy = Options.UseProxy ? RuriLib.Models.Proxies.Proxy.Parse(Options.TestProxy, Options.ProxyType) : null;

        var providers = BotRuntimeContextBuilder.CreateDebuggerProviders(
            RuriLibSettings,
            RNGProvider,
            RandomUAProvider);

        // Ensure the debugger respects the current VerboseMode setting coming from the global RuriLibSettingsService
        if (RuriLibSettings?.RuriLibSettings?.GeneralSettings != null)
        {
            Config.Settings.GeneralSettings.VerboseMode = RuriLibSettings.RuriLibSettings.GeneralSettings.VerboseMode;
        }

        // Unregister the previous event if there was an existing stepper
        if (_stepper != null)
        {
            _stepper.WaitingForStep -= OnWaitingForStep;
        }

        _stepper = new Stepper();
        _stepper.WaitingForStep += OnWaitingForStep;

        var runtimeContext = _botSessionFactory.CreateRuntimeContext(
            Config.Settings.DataSettings.Resources,
            ownerId: 0,
            jobId: 0,
            includePythonEngine: false,
            logger: Logger,
            continueOnResourceError: true);

        // Build the BotData
        _data = _botSessionFactory.CreateBotData(
            providers,
            Config.Settings,
            Logger,
            dataLine,
            proxy,
            Options.UseProxy,
            _cts.Token,
            _stepper,
            runtimeContext.AsyncLocker,
            runtimeContext.HttpClient);

        // Scripts are already built above

        // Optimized slice logging with single enumeration and reusable StringBuilder
        var variables = dataLine.GetVariables().ToList();
        _logBuilder.Clear();
        _logBuilder.AppendLine($"Sliced {dataLine.Data} into:");

        var urlEncode = _data.ConfigSettings.DataSettings.UrlEncodeDataAfterSlicing;
        foreach (var slice in variables)
        {
            var sliceValue = urlEncode ? Uri.EscapeDataString(slice.AsString()) : slice.AsString();
            _logBuilder.AppendLine($"{slice.Name}: {sliceValue}");
        }
        Logger.Log(_logBuilder.ToString());

        var globals = runtimeContext.GlobalVariables;
        var scriptGlobals = new ScriptGlobals(_data, globals);

        // Set custom inputs efficiently
        var customInputs = Config.Settings.InputSettings.CustomInputs;
        if (customInputs.Count > 0)
        {
            var inputDict = (IDictionary<string, object>)scriptGlobals.input;
            foreach (var input in customInputs)
            {
                inputDict.Add(input.VariableName, input.DefaultAnswer);
            }
        }



        try
        {
            sw.Start();
            ChangeStatus(ConfigDebuggerStatus.Running);


            // If the startup script is not empty, execute it
            if (!string.IsNullOrWhiteSpace(Config.StartupCSharpScript) && startupScript is not null)
            {
                // This data is temporary and will not be persisted to the bots, it is
                // only used in this context to be able to use variables e.g. data.SOURCE
                // and other things like providers, settings, logger.
                // By default it doesn't support proxies.
                var startupData = _botSessionFactory.CreateStartupBotData(
                    providers,
                    Config.Settings,
                    Logger,
                    wordlistType,
                    _cts.Token,
                    _stepper,
                    runtimeContext.AsyncLocker,
                    runtimeContext.HttpClient);

                Logger.Log("Executing startup script...");
                await _scriptPreparation
                    .ExecuteStartupScriptAsync(startupScript, startupData, globals, _cts.Token)
                    .ConfigureAwait(false);
                Logger.Log("Executing main script...");
            }

            var scriptVars = await script.RunAsync(scriptGlobals, _cts.Token).ConfigureAwait(false);

            // Optimized variable processing with early filtering
            var markedForCapture = _data.MarkedForCapture;
            if (scriptVars != null)
            {
                foreach (var scriptVar in scriptVars)
                {
                    // Early exit for temporary variables
                    if (scriptVar.Key.StartsWith("tmp_")) continue;

                    try
                    {
                        var actualType = scriptVar.Value?.GetType();
                        VariableType? vType;

                        try
                        {
                            vType = DescriptorsRepository.ToVariableType(actualType);
                        }
                        catch (InvalidCastException) when (scriptVar.Value != null)
                        {
                            actualType = scriptVar.Value.GetType();
                            vType = DescriptorsRepository.ToVariableType(actualType);
                        }

                        if (vType.HasValue)
                        {
                            var variable = DescriptorsRepository.ToVariable(scriptVar.Key, actualType, scriptVar.Value);
                            variable.MarkedForCapture = markedForCapture.Contains(scriptVar.Key);
                            Variables.Add(variable);
                        }
                    }
                    catch
                    {
                        // Unsupported types are ignored
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _data.STATUS = "ERROR";
            Logger.Log("Operation canceled", LogColors.Tomato);
        }
        catch (Exception ex)
        {
            _data.STATUS = "ERROR";

            // Simplified error handling
            Logger.Log($"❌ {ex.GetType().Name}: {ex.Message}", LogColors.Tomato);
            if (RuriLibSettings.RuriLibSettings.GeneralSettings.VerboseMode)
            {
                Logger.Log(ex.StackTrace ?? string.Empty, LogColors.Gray);
            }

            ChangeStatus(ConfigDebuggerStatus.Idle);
            throw;
        }
        finally
        {
            sw.Stop();

            Logger.Log($"BOT ENDED AFTER {sw.ElapsedMilliseconds} ms WITH STATUS: {_data.STATUS}");

            // Save the browsers for later use if they were set during this run
            _lastPuppeteerBrowser = _data.PuppeteerSession.Browser as Browser;
            _lastSeleniumBrowser = _data.TryGetObject<OpenQA.Selenium.WebDriver>("selenium");
            _lastPlaywrightBrowser = _data.PlaywrightSession.Browser;
            _lastPlaywrightInstance = _data.PlaywrightSession.Instance;

            // Dispose stuff in data.Objects
            // We only want to dispose of general objects, not browser objects that are managed by the debugger itself
            _data.DisposeObjectsExcept(["ironPyEngine", "selenium", "seleniumDriver"]);

            // Dispose resources - fixed: use ToList() to avoid modification during iteration
            foreach (var resource in runtimeContext.Resources.Values.OfType<IDisposable>().ToList())
            {
                try
                {
                    resource?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error disposing resource: {ex.Message}", LogColors.Tomato);
                }
            }

            runtimeContext.AsyncLocker.Dispose();

            ChangeStatus(ConfigDebuggerStatus.Idle);
        }

        if (_stepper is not null)
        {
            _stepper.WaitingForStep -= OnWaitingForStep;
        }

        // Completely remove browser disposal from here to ensure the browser persists.
        // The browser will now only be closed by the application's overall shutdown
        // or by the logic at the start of a new debug session.

        // GC.SuppressFinalize(this);
    }

    private static ScriptPreparationResult PrepareScripts(
        Config config, bool stepByStep, PluginRepository pluginRepo)
    {
        var (transpiledScript, transpiledStartupScript) = GetTranspiledScripts(config, stepByStep);
        return _scriptPreparation
            .PrepareAsync(
                config,
                pluginRepo,
                stepByStep: stepByStep,
                preparedSources: new PreparedScriptSources(transpiledScript, transpiledStartupScript))
            .GetAwaiter()
            .GetResult();
    }

    private static (string mainScript, string startupScript) GetTranspiledScripts(Config config, bool stepByStep)
    {
        if (config.Mode is not (ConfigMode.Stack or ConfigMode.LoliCode))
        {
            return (config.CSharpScript ?? string.Empty, config.StartupCSharpScript ?? string.Empty);
        }

        var cacheKey = BuildTranspilationCacheKey(config, stepByStep);
        if (_transpilationCache.TryGetValue(cacheKey, out var cached))
        {
            cached.LastAccessedUtc = DateTime.UtcNow;
            return (cached.MainScript, cached.StartupScript);
        }

        var transpiledScripts = _scriptPreparation.TranspileSources(config, stepByStep);
        var mainScript = transpiledScripts.MainScript;
        var startupScript = transpiledScripts.StartupScript;

        _transpilationCache[cacheKey] = new CachedTranspilation
        {
            MainScript = mainScript,
            StartupScript = startupScript,
            LastAccessedUtc = DateTime.UtcNow
        };

        EvictTranspilationCacheIfNeeded();
        return (mainScript, startupScript);
    }

    private static string BuildTranspilationCacheKey(Config config, bool stepByStep)
    {
        var settingsHash = ComputeHash(JsonConvert.SerializeObject(config.Settings));
        var startupHash = ComputeHash(config.StartupLoliCodeScript ?? string.Empty);

        return config.Mode switch
        {
            ConfigMode.LoliCode => $"L|{(stepByStep ? 1 : 0)}|{settingsHash}|{ComputeHash(config.LoliCodeScript ?? string.Empty)}|{startupHash}",
            ConfigMode.Stack => $"S|{(stepByStep ? 1 : 0)}|{settingsHash}|{ComputeHash(JsonConvert.SerializeObject(config.Stack))}|{startupHash}",
            _ => $"C|{(stepByStep ? 1 : 0)}|{settingsHash}|{ComputeHash(config.CSharpScript ?? string.Empty)}|{ComputeHash(config.StartupCSharpScript ?? string.Empty)}"
        };
    }

    private static string ComputeHash(string input)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static void EvictTranspilationCacheIfNeeded()
    {
        var overflow = _transpilationCache.Count - MaxTranspilationCacheSize;
        if (overflow <= 0)
        {
            return;
        }

        foreach (var key in _transpilationCache
                     .OrderBy(kvp => kvp.Value.LastAccessedUtc)
                     .Take(overflow)
                     .Select(kvp => kvp.Key)
                     .ToList())
        {
            _transpilationCache.TryRemove(key, out _);
        }
    }


    /// <summary>
    /// Thread-safe status change with performance optimization.
    /// </summary>
    private void ChangeStatus(ConfigDebuggerStatus newStatus)
    {
        lock (_statusLock)
        {
            if (Status == newStatus) return; // Avoid unnecessary events
            Status = newStatus;
        }
        StatusChanged?.Invoke(this, newStatus);
    }

    /// <summary>
    /// Tries to take a step. Returns true if a step was taken.
    /// </summary>
    public bool TryTakeStep()
    {
        if (_stepper?.IsWaiting != true)
        {
            return false;
        }

        ChangeStatus(ConfigDebuggerStatus.Running);
        return _stepper.TryTakeStep();
    }

    public void Stop() => _cts?.Cancel();

    /// <summary>
    /// Propagate the events
    /// </summary>
    private void OnNewEntry(object sender, BotLoggerEntry entry) => NewLogEntry?.Invoke(this, entry);
    private void OnWaitingForStep(object sender, EventArgs e)
    {
        ChangeStatus(ConfigDebuggerStatus.WaitingForStep);
    }

    public void Dispose()
    {
        Logger.NewEntry -= OnNewEntry;

        if (_stepper is not null)
        {
            _stepper.WaitingForStep -= OnWaitingForStep;
        }

        // Dispose browsers
        _lastPuppeteerBrowser?.CloseAsync().ConfigureAwait(false);
        _lastPuppeteerBrowser?.DisposeAsync();
        _lastSeleniumBrowser?.Quit();
        _lastSeleniumBrowser?.Dispose();
        _lastPlaywrightBrowser?.CloseAsync().ConfigureAwait(false);
        _lastPlaywrightInstance?.Dispose();

        GC.SuppressFinalize(this);
    }

}
