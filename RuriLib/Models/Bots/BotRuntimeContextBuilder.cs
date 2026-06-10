using IronPython.Compiler;
using IronPython.Hosting;
using IronPython.Runtime;
using Microsoft.CodeAnalysis.Scripting;
using RuriLib.Helpers;
using RuriLib.Logging;
using RuriLib.Models.Configs;
using RuriLib.Models.Data;
using RuriLib.Models.Data.Resources;
using RuriLib.Models.Data.Resources.Options;
using RuriLib.Models.Proxies;
using RuriLib.Models.Scripting;
using RuriLib.Providers.UserAgents;
using RuriLib.Services;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Models.Bots;

internal static class BotRuntimeContextBuilder
{
    public static Providers CreateDebuggerProviders(
        RuriLibSettingsService settings,
        IRandomUAProvider? randomUaProvider)
    {
        var providers = new Providers(settings);

        if (settings?.RuriLibSettings?.GeneralSettings?.UseCustomUserAgentsList == false && randomUaProvider is not null)
        {
            providers.RandomUA = randomUaProvider;
        }

        return providers;
    }

    public static BotRuntimeContext CreateContext(
        IReadOnlyList<ConfigResourceOptions> resourceOptions,
        int ownerId,
        int jobId,
        bool includePythonEngine,
        AsyncLocker? asyncLocker = null,
        IBotLogger? logger = null,
        bool continueOnResourceError = false)
    {
        var resources = CreateResources(resourceOptions, logger, continueOnResourceError);

        dynamic globals = new ExpandoObject();
        globals.Resources = resources;
        globals.OwnerId = ownerId;
        globals.JobId = jobId;

        return new BotRuntimeContext
        {
            AsyncLocker = asyncLocker ?? new AsyncLocker(),
            Resources = resources,
            HttpClient = new HttpClient(),
            GlobalVariables = globals,
            PythonEngine = includePythonEngine ? CreatePythonEngine() : null
        };
    }

    public static BotData CreateBotData(BotRuntimeSessionOptions options)
    {
        var botData = new BotData(
            options.Providers,
            options.ConfigSettings,
            options.Logger,
            options.Line,
            options.Proxy,
            options.UseProxy)
        {
            CancellationToken = options.CancellationToken,
            Stepper = options.Stepper,
            AsyncLocker = options.AsyncLocker
        };

        if (options.SharedHttpClient is not null)
        {
            botData.SetObject("httpClient", options.SharedHttpClient, disposeExisting: false);
        }

        return botData;
    }

    public static async Task ExecuteStartupScriptAsync(
        IScript startupScript,
        BotData startupData,
        dynamic globalVariables,
        CancellationToken cancellationToken)
    {
        var startupGlobals = new ScriptGlobals(startupData, globalVariables);
        _ = await startupScript.RunAsync(startupGlobals, cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, ConfigResource> CreateResources(
        IReadOnlyList<ConfigResourceOptions> resourceOptions,
        IBotLogger? logger,
        bool continueOnResourceError)
    {
        var resources = new Dictionary<string, ConfigResource>(resourceOptions.Count);

        foreach (var option in resourceOptions)
        {
            try
            {
                resources[option.Name] = option switch
                {
                    LinesFromFileResourceOptions lines => new LinesFromFileResource(lines),
                    RandomLinesFromFileResourceOptions randomLines => new RandomLinesFromFileResource(randomLines),
                    _ => throw new NotImplementedException()
                };
            }
            catch
            {
                if (!continueOnResourceError)
                {
                    throw;
                }

                logger?.Log($"Could not create resource {option.Name}", LogColors.Tomato);
            }
        }

        return resources;
    }

    private static Lazy<dynamic> CreatePythonEngine()
        => new(() =>
        {
            var runtime = Python.CreateRuntime();
            var pyengine = runtime.GetEngine("py");
            var pco = (PythonCompilerOptions)pyengine.GetCompilerOptions();
            pco.Module &= ~ModuleOptions.Optimized;
            return pyengine;
        });
}

internal sealed class BotRuntimeContext
{
    public required AsyncLocker AsyncLocker { get; init; }
    public required Dictionary<string, ConfigResource> Resources { get; init; }
    public required HttpClient HttpClient { get; init; }
    public required dynamic GlobalVariables { get; init; }
    public Lazy<dynamic>? PythonEngine { get; init; }
}

internal sealed class BotRuntimeSessionOptions
{
    public required Providers Providers { get; init; }
    public required ConfigSettings ConfigSettings { get; init; }
    public required IBotLogger Logger { get; init; }
    public required DataLine Line { get; init; }
    public Proxy? Proxy { get; init; }
    public bool UseProxy { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public Stepper? Stepper { get; init; }
    public AsyncLocker? AsyncLocker { get; init; }
    public HttpClient? SharedHttpClient { get; init; }
}
