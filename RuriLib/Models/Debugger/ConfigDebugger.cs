using IronPython.Compiler;
using IronPython.Hosting;
using IronPython.Runtime;
using Microsoft.CodeAnalysis.Scripting;
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Net.Http;
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

    public List<Variable> Variables { get; } = new();

    public ConfigDebugger(Config config, DebuggerOptions options = null, BotLogger logger = null)
    {
        Config = config;
        Options = options ?? new DebuggerOptions();
        Logger = logger ?? new BotLogger();
        Logger.NewEntry += OnNewEntry;
    }

    public async Task Run()
    {
        // Build scripts
        if (Config.Mode is ConfigMode.Stack or ConfigMode.LoliCode)
        {
            Config.CSharpScript = Config.Mode == ConfigMode.Stack
                ? Stack2CSharpTranspiler.Transpile(Config.Stack, Config.Settings, Options.StepByStep)
                : Loli2CSharpTranspiler.Transpile(Config.LoliCodeScript, Config.Settings, Options.StepByStep);

            Config.StartupCSharpScript = Loli2CSharpTranspiler.Transpile(Config.StartupLoliCodeScript, Config.Settings, Options.StepByStep);
        }

        var scriptBuilder = new ScriptBuilder();
        var script = scriptBuilder.Build(Config.CSharpScript, Config.Settings.ScriptSettings, PluginRepo);
        IScript startupScript = null;
        if (!string.IsNullOrWhiteSpace(Config.StartupCSharpScript))
        {
            startupScript = scriptBuilder.Build(Config.StartupCSharpScript, Config.Settings.ScriptSettings, PluginRepo);
        }

        if (Options.UseProxy && !Options.TestProxy.Contains(':'))
        {
            throw new InvalidProxyException(Options.TestProxy);
        }

        if (!Options.PersistLog)
        {
            Logger.Clear();
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

        var providers = new Bots.Providers(RuriLibSettings)
        {
            RNG = RNGProvider
        };

        // Ensure the debugger respects the current VerboseMode setting coming from the global RuriLibSettingsService
        if (RuriLibSettings?.RuriLibSettings?.GeneralSettings != null)
        {
            Config.Settings.GeneralSettings.VerboseMode = RuriLibSettings.RuriLibSettings.GeneralSettings.VerboseMode;
        }

        if (RuriLibSettings?.RuriLibSettings?.GeneralSettings?.UseCustomUserAgentsList == false)
        {
            providers.RandomUA = RandomUAProvider;
        }

        // Unregister the previous event if there was an existing stepper
        if (_stepper != null)
        {
            _stepper.WaitingForStep -= OnWaitingForStep;
        }

        _stepper = new Stepper();
        _stepper.WaitingForStep += OnWaitingForStep;

        // Build the BotData
        _data = new BotData(providers, Config.Settings, Logger, dataLine, proxy, Options.UseProxy)
        {
            CancellationToken = _cts.Token,
            Stepper = _stepper
        };

        // Use single HttpClient instance with proper disposal
        var httpClient = new HttpClient();
        _data.SetObject("httpClient", httpClient);

        _data.AsyncLocker = new();
        dynamic globals = new ExpandoObject();

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

        // Initialize resources with capacity hint
        var resources = new Dictionary<string, ConfigResource>(Config.Settings.DataSettings.Resources.Count);

        // Resources will need to be disposed of
        foreach (var opt in Config.Settings.DataSettings.Resources)
        {
            try
            {
                resources[opt.Name] = opt switch
                {
                    LinesFromFileResourceOptions x => new LinesFromFileResource(x),
                    RandomLinesFromFileResourceOptions x => new RandomLinesFromFileResource(x),
                    _ => throw new NotImplementedException()
                };
            }
            catch
            {
                Logger.Log($"Could not create resource {opt.Name}", LogColors.Tomato);
            }
        }

        // Add resources to global variables
        globals.Resources = resources;
        globals.OwnerId = 0;
        globals.JobId = 0;
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
            if (!string.IsNullOrWhiteSpace(Config.StartupCSharpScript))
            {
                // This data is temporary and will not be persisted to the bots, it is
                // only used in this context to be able to use variables e.g. data.SOURCE
                // and other things like providers, settings, logger.
                // By default it doesn't support proxies.
                var startupData = new BotData(providers, Config.Settings, Logger,
                    new DataLine(string.Empty, wordlistType), null, false)
                {
                    CancellationToken = _cts.Token,
                    Stepper = _stepper
                };

                Logger.Log("Executing startup script...");
                var startupGlobals = new ScriptGlobals(startupData, globals);
                _ = await startupScript.RunAsync(startupGlobals, _cts.Token).ConfigureAwait(false);
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
            _lastPuppeteerBrowser = _data.TryGetObject<Browser>("puppeteer");
            _lastSeleniumBrowser = _data.TryGetObject<OpenQA.Selenium.WebDriver>("selenium");
            _lastPlaywrightBrowser = _data.TryGetObject<Microsoft.Playwright.IBrowser>("playwright");
            _lastPlaywrightInstance = _data.TryGetObject<Microsoft.Playwright.IPlaywright>("playwrightInstance");

            // Dispose stuff in data.Objects
            // We only want to dispose of general objects, not browser objects that are managed by the debugger itself
            _data.DisposeObjectsExcept(["ironPyEngine", "puppeteer", "puppeteerPage", "puppeteerFrame", "selenium", "seleniumDriver", "playwright", "playwrightPage", "playwrightInstance"]);

            // Dispose resources - fixed: use ToList() to avoid modification during iteration
            foreach (var resource in resources.Values.OfType<IDisposable>().ToList())
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

            _data.AsyncLocker.Dispose();

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
