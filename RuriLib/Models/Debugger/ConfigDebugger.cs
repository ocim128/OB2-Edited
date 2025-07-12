using IronPython.Compiler;
using IronPython.Hosting;
using IronPython.Runtime;
using PuppeteerSharp;
using RuriLib.Exceptions;
using RuriLib.Helpers;
using RuriLib.Helpers.Blocks;
using RuriLib.Helpers.CSharp;
using RuriLib.Helpers.Transpilers;
using RuriLib.Legacy.LS;
using RuriLib.Legacy.Models;
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
using System.Threading;
using System.Threading.Tasks;

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

    public ConfigDebugger(Config config, DebuggerOptions options = null, BotLogger logger = null)
    {
        Config = config;
        Options = options ?? new DebuggerOptions();
        Logger = logger ?? new BotLogger();
        Logger.NewEntry += OnNewEntry;
    }

    public async Task Run()
    {
        // Build the C# script if in Stack or LoliCode mode
        if (Config.Mode is ConfigMode.Stack or ConfigMode.LoliCode)
        {
            Config.CSharpScript = Config.Mode == ConfigMode.Stack
                ? Stack2CSharpTranspiler.Transpile(Config.Stack, Config.Settings, Options.StepByStep)
                : Loli2CSharpTranspiler.Transpile(Config.LoliCodeScript, Config.Settings, Options.StepByStep);

            // Stacker is not currently available for the startup phase
            Config.StartupCSharpScript = Loli2CSharpTranspiler.Transpile(Config.StartupLoliCodeScript, Config.Settings, Options.StepByStep);
        }

        if (Options.UseProxy && !Options.TestProxy.Contains(':'))
        {
            throw new InvalidProxyException(Options.TestProxy);
        }

        if (!Options.PersistLog)
        {
            Logger.Clear();
        }

        // Close any previously opened browsers
        if (_lastPuppeteerBrowser != null)
        {
            await _lastPuppeteerBrowser.CloseAsync().ConfigureAwait(false);
            await _lastPuppeteerBrowser.DisposeAsync();
            _lastPuppeteerBrowser = null;
        }

        if (_lastSeleniumBrowser != null)
        {
            _lastSeleniumBrowser.Quit();
            _lastSeleniumBrowser.Dispose();
            _lastSeleniumBrowser = null;
        }

        Options.Variables.Clear();
        Status = ConfigDebuggerStatus.Running;
        _cts = new CancellationTokenSource();
        var sw = new Stopwatch();

        var wordlistType = RuriLibSettings.Environment.WordlistTypes.First(w => w.Name == Options.WordlistType);
        var dataLine = new DataLine(Options.TestData, wordlistType);
        var proxy = Options.UseProxy ? Proxy.Parse(Options.TestProxy, Options.ProxyType) : null;

        var providers = new Bots.Providers(RuriLibSettings)
        {
            RNG = RNGProvider
        };

        // Ensure the debugger respects the current VerboseMode setting coming from the global RuriLibSettingsService
        if (RuriLibSettings != null)
        {
            Config.Settings.GeneralSettings.VerboseMode = RuriLibSettings.RuriLibSettings.GeneralSettings.VerboseMode;
        }

        if (!RuriLibSettings.RuriLibSettings.GeneralSettings.UseCustomUserAgentsList)
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
        using var httpClient = new HttpClient();
        _data.SetObject("httpClient", httpClient);
        var runtime = Python.CreateRuntime();
        var pyengine = runtime.GetEngine("py");
        var pco = (PythonCompilerOptions)pyengine.GetCompilerOptions();
        pco.Module &= ~ModuleOptions.Optimized;
        _data.SetObject("ironPyEngine", pyengine);
        _data.AsyncLocker = new();

        dynamic globals = new ExpandoObject();

        var script = new ScriptBuilder()
            .Build(Config.CSharpScript, Config.Settings.ScriptSettings, PluginRepo);

        var startupScript = new ScriptBuilder().Build(Config.StartupCSharpScript, Config.Settings.ScriptSettings, PluginRepo);

        Logger.Log($"Sliced {dataLine.Data} into:");
        foreach (var slice in dataLine.GetVariables())
        {
            var sliceValue = _data.ConfigSettings.DataSettings.UrlEncodeDataAfterSlicing
                ? Uri.EscapeDataString(slice.AsString())
                : slice.AsString();

            Logger.Log($"{slice.Name}: {sliceValue}");
        }

        // Initialize resources
        Dictionary<string, ConfigResource> resources = [];

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

        // Set custom inputs
        foreach (var input in Config.Settings.InputSettings.CustomInputs)
        {
            (scriptGlobals.input as IDictionary<string, object>).Add(input.VariableName, input.DefaultAnswer);
        }

        // [LEGACY] Set up the VariablesList
        if (Config.Mode == ConfigMode.Legacy)
        {
            var slices = new List<Variable>();

            foreach (var slice in dataLine.GetVariables())
            {
                var sliceValue = _data.ConfigSettings.DataSettings.UrlEncodeDataAfterSlicing
                    ? Uri.EscapeDataString(slice.AsString())
                    : slice.AsString();

                slices.Add(new StringVariable(sliceValue) { Name = slice.Name });
            }

            var legacyVariables = new VariablesList(slices);

            foreach (var input in Config.Settings.InputSettings.CustomInputs)
            {
                legacyVariables.Set(new StringVariable(input.DefaultAnswer) { Name = input.VariableName });
            }

            _data.SetObject("legacyVariables", legacyVariables);
        }

        try
        {
            sw.Start();
            StatusChanged?.Invoke(this, ConfigDebuggerStatus.Running);

            if (Config.Mode != ConfigMode.Legacy)
            {
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
                    _ = await startupScript.RunAsync(startupGlobals, null, _cts.Token).ConfigureAwait(false);
                    Logger.Log("Executing main script...");
                }

                var state = await script.RunAsync(scriptGlobals, null, _cts.Token).ConfigureAwait(false);

                foreach (var scriptVar in state.Variables)
                {
                    try
                    {
                        // Determine variable type: use declared type or fallback to runtime type for dynamic variables
                        var declaredType = scriptVar.Type;
                        var actualType = declaredType;
                        VariableType? vType;
                        try
                        {
                            vType = DescriptorsRepository.ToVariableType(declaredType);
                        }
                        catch (InvalidCastException)
                        {
                            if (scriptVar.Value != null)
                            {
                                actualType = scriptVar.Value.GetType();
                            }

                            vType = DescriptorsRepository.ToVariableType(actualType);
                        }
                        if (vType.HasValue && !scriptVar.Name.StartsWith("tmp_"))
                        {
                            var variable = DescriptorsRepository.ToVariable(scriptVar.Name, actualType, scriptVar.Value);
                            variable.MarkedForCapture = _data.MarkedForCapture.Contains(scriptVar.Name);
                            Options.Variables.Add(variable);
                        }
                    }
                    catch
                    {
                        // Unsupported types are ignored
                    }
                }
            }
            else
            {
                // [LEGACY] Run the LoliScript in the old way
                var loliScript = new LoliScript(Config.LoliScript);
                var lsGlobals = new LSGlobals(_data);

                do
                {
                    if (_cts.IsCancellationRequested)
                    {
                        break;
                    }

                    await loliScript.TakeStep(lsGlobals).ConfigureAwait(false);

                    Options.Variables.Clear();
                    var legacyVariables = _data.TryGetObject<VariablesList>("legacyVariables");
                    Options.Variables.AddRange(legacyVariables.Variables);
                    Options.Variables.AddRange(lsGlobals.Globals.Variables);
                }
                while (loliScript.CanProceed);
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

            // Enhanced error handling with detailed verbose output, especially for compilation errors
            if (ex.GetType().Name.Contains("CompilationError"))
            {
                var errorAlreadyLogged = false;
                try
                {
                    var csharpScript = Config.CSharpScript;
                    var lines = csharpScript.Split('\n');

                    // Try to extract C# line number from error message ("(line,col)")
                    var errorMatch = MyRegex().Match(ex.Message);
                    if (errorMatch.Success && int.TryParse(errorMatch.Groups[1].Value, out var csharpLineNumber))
                    {
                        csharpLineNumber--; // Convert to 0-based index

                        // Look for the closest LoliCode line comment going backwards from the error line
                        var loliCodeLineNumber = -1;
                        for (var i = csharpLineNumber; i >= 0; i--)
                        {
                            if (i < lines.Length)
                            {
                                var commentMatch = MyRegex1().Match(lines[i]);
                                if (commentMatch.Success && int.TryParse(commentMatch.Groups[1].Value, out loliCodeLineNumber))
                                {
                                    break;
                                }
                            }
                        }

                        if (loliCodeLineNumber > 0)
                        {
                            // Concise error description
                            var conciseError = ex.Message.Contains("error CS")
                                ? ex.Message[ex.Message.IndexOf("error CS")..]
                                : ex.Message.Split(':').LastOrDefault()?.Trim();
                            Logger.Log($"❌ Compilation Error at Line {loliCodeLineNumber}: {conciseError}", LogColors.Tomato);

                            // Problematic LoliCode line
                            try
                            {
                                if (!string.IsNullOrEmpty(Config.LoliCodeScript))
                                {
                                    var loliCodeLines = Config.LoliCodeScript.Split('\n');
                                    if (loliCodeLineNumber > 0 && loliCodeLineNumber <= loliCodeLines.Length)
                                    {
                                        Logger.Log($"📍 {loliCodeLines[loliCodeLineNumber - 1].Trim()}", LogColors.Yellow);
                                    }
                                }
                            }
                            catch { /* ignore failures here */ }

                            // Surrounding generated C# (only if verbose mode)
                            if (RuriLibSettings.RuriLibSettings.GeneralSettings.VerboseMode)
                            {
                                Logger.Log($"📝 Generated C# code around error (line {csharpLineNumber + 1}):", LogColors.Gray);
                                var start = Math.Max(0, csharpLineNumber - 2);
                                var end = Math.Min(lines.Length - 1, csharpLineNumber + 2);
                                for (var i = start; i <= end; i++)
                                {
                                    var marker = i == csharpLineNumber ? ">>> " : "    ";
                                    Logger.Log($"{marker}{i + 1:D3}: {lines[i].TrimEnd()}", LogColors.Gray);
                                }
                            }

                            errorAlreadyLogged = true;
                        }
                    }

                    // Fallback to simple error reporting if detailed mapping was not possible
                    if (!errorAlreadyLogged)
                    {
                        Logger.Log($"❌ Compilation Error: {ex.Message}", LogColors.Tomato);

                        if (RuriLibSettings.RuriLibSettings.GeneralSettings.VerboseMode)
                        {
                            Logger.Log("📝 Generated C# code:", LogColors.Gray);
                            for (var i = 0; i < lines.Length; i++)
                            {
                                Logger.Log($"{i + 1:D3}: {lines[i].TrimEnd()}", LogColors.Gray);
                            }
                        }
                    }
                }
                catch
                {
                    // Final fallback if everything else failed
                    Logger.Log($"❌ Compilation Error: {ex.Message}", LogColors.Tomato);
                }
            }
            else
            {
                // Non-compilation errors
                Logger.Log($"❌ {ex.GetType().Name}: {ex.Message}", LogColors.Tomato);
                if (RuriLibSettings.RuriLibSettings.GeneralSettings.VerboseMode)
                {
                    Logger.Log(ex.StackTrace ?? string.Empty, LogColors.Gray);
                }
            }

            Status = ConfigDebuggerStatus.Idle;
            throw;
        }
        finally
        {
            sw.Stop();

            Logger.Log($"BOT ENDED AFTER {sw.ElapsedMilliseconds} ms WITH STATUS: {_data.STATUS}");

            // Save the browsers for later use if they were set during this run
            _lastPuppeteerBrowser = _data.TryGetObject<Browser>("puppeteer");
            _lastSeleniumBrowser = _data.TryGetObject<OpenQA.Selenium.WebDriver>("selenium");

            // Dispose stuff in data.Objects
            // We only want to dispose of general objects, not browser objects that are managed by the debugger itself
            _data.DisposeObjectsExcept(["httpClient", "ironPyEngine", "puppeteer", "puppeteerPage", "puppeteerFrame", "selenium", "seleniumDriver"]);

            // Dispose resources
            foreach (var resource in resources.Where(r => r.Value is IDisposable)
                .Select(r => r.Value).Cast<IDisposable>())
            {
                resource.Dispose();
            }

            _data.AsyncLocker.Dispose();

            Status = ConfigDebuggerStatus.Idle;
            StatusChanged?.Invoke(this, ConfigDebuggerStatus.Idle);
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
    /// Tries to take a step. Returns true if a step was taken.
    /// </summary>
    public bool TryTakeStep()
    {
        if (_stepper?.IsWaiting != true)
        {
            return false;
        }

        StatusChanged?.Invoke(this, ConfigDebuggerStatus.Running);
        return _stepper.TryTakeStep();
    }

    public void Stop() => _cts.Cancel();

    /// <summary>
    /// Propagate the events
    /// </summary>
    private void OnNewEntry(object sender, BotLoggerEntry entry) => NewLogEntry?.Invoke(this, entry);
    private void OnWaitingForStep(object sender, EventArgs e)
    {
        Status = ConfigDebuggerStatus.WaitingForStep;
        StatusChanged?.Invoke(this, ConfigDebuggerStatus.WaitingForStep);
    }

    public void Dispose()
    {
        Logger.NewEntry -= OnNewEntry;

        if (_stepper is not null)
        {
            _stepper.WaitingForStep -= OnWaitingForStep;
        }

        if (!Config.Settings.BrowserSettings.Headless || (Config.Settings.BrowserSettings.CommandLineArgs != "--disable-notifications" && !string.IsNullOrWhiteSpace(Config.Settings.BrowserSettings.CommandLineArgs)))
        {
            _lastPuppeteerBrowser?.Dispose();
            _lastSeleniumBrowser?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\((\d+),\d+\)")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
    [System.Text.RegularExpressions.GeneratedRegex(@"// LoliCode line (\d+):")]
    private static partial System.Text.RegularExpressions.Regex MyRegex1();
}
