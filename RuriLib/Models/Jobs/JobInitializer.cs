using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using RuriLib.Helpers;
using RuriLib.Helpers.CSharp;
using RuriLib.Helpers.Transpilers;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs;
using RuriLib.Models.Data;
using RuriLib.Models.Jobs.Execution;
using RuriLib.Models.Proxies;
using RuriLib.Models.Scripting;
using RuriLib.Services;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Models.Jobs;

internal sealed class JobInitializer
{
    public async Task<JobInitializationResult> InitializeAsync(
        MultiRunJob job,
        RuriLibSettingsService settings,
        PluginRepository pluginRepo,
        CancellationToken cancellationToken)
    {
        ValidateJobParameters(job);

        var asyncLocker = new AsyncLocker();

        job.DataPool.Reload();

        var proxyState = MultiRunJob.ShouldUseProxies(job.ProxyMode, job.Config.Settings.ProxySettings)
            ? await InitializeProxyCoordinatorAsync(job, asyncLocker, cancellationToken).ConfigureAwait(false)
            : (ProxyPool: default(ProxyPool), ExecutionCoordinator: InitializeCoordinatorWithoutProxy(job));

        job.Providers.Security.X509RevocationMode = job.Config.Mode == ConfigMode.DLL
            ? System.Security.Cryptography.X509Certificates.X509RevocationMode.Online
            : System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;

        var runtimeContext = BotRuntimeContextBuilder.CreateContext(
            job.Config.Settings.DataSettings.Resources,
            job.OwnerId,
            job.Id,
            includePythonEngine: true,
            asyncLocker: asyncLocker);
        var scriptState = await InitializeScriptAsync(job.Config, pluginRepo, cancellationToken).ConfigureAwait(false);
        await InitializeGlobalsAsync(
            job,
            settings,
            pluginRepo,
            runtimeContext,
            cancellationToken).ConfigureAwait(false);

        return new JobInitializationResult
        {
            AsyncLocker = asyncLocker,
            ProxyPool = proxyState.ProxyPool,
            ExecutionCoordinator = proxyState.ExecutionCoordinator,
            Resources = runtimeContext.Resources,
            HttpClient = runtimeContext.HttpClient,
            PythonEngine = runtimeContext.PythonEngine!,
            GlobalVariables = runtimeContext.GlobalVariables,
            DllMethod = scriptState.DllMethod,
            Script = scriptState.Script
        };
    }

    private static void ValidateJobParameters(MultiRunJob job)
    {
        if (job.Config?.Settings?.DataSettings == null)
        {
            throw new ArgumentNullException(nameof(job.Config));
        }

        if (job.DataPool == null)
        {
            throw new ArgumentNullException(nameof(job.DataPool));
        }

        if (job.Skip >= job.DataPool.Size)
        {
            throw new ArgumentException("Skip must be smaller than data pool size");
        }

        if (MultiRunJob.ShouldUseProxies(job.ProxyMode, job.Config.Settings.ProxySettings) &&
            (job.ProxySources?.Count ?? 0) == 0)
        {
            throw new ArgumentNullException(nameof(job.ProxySources));
        }

        if (!job.Config.Settings.DataSettings.AllowedWordlistTypes.Contains(job.DataPool.WordlistType))
        {
            throw new NotSupportedException($"Config does not support wordlist type: {job.DataPool.WordlistType}");
        }
    }

    private static async Task<(ProxyPool ProxyPool, BotExecutionCoordinator ExecutionCoordinator)> InitializeProxyCoordinatorAsync(
        MultiRunJob job,
        AsyncLocker asyncLocker,
        CancellationToken cancellationToken)
    {
        job.ProxySources.ForEach(p => p.UserId = job.OwnerId);

        var proxyPool = new ProxyPool(
            job.ProxySources,
            new ProxyPoolOptions { AllowedTypes = job.Config.Settings.ProxySettings.AllowedProxyTypes });

        IDisposable? releaser = null;
        try
        {
            releaser = await asyncLocker
                .Acquire(typeof(ProxyPool), nameof(ProxyPool.ReloadAllAsync), cancellationToken)
                .ConfigureAwait(false);

            await proxyPool.ReloadAllAsync(job.ShuffleProxies, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            releaser?.Dispose();
        }

        if (!proxyPool.Proxies.Any())
        {
            proxyPool.Dispose();
            throw new InvalidOperationException(
                "No proxies that respect the allowed types are available, but the job is set to use proxies");
        }

        var executionHandler = ExecutionHandlerFactory.CreateHandler(job.Config.Mode);
        return (proxyPool, new BotExecutionCoordinator(executionHandler, new ProxyManager(job, asyncLocker)));
    }

    private static BotExecutionCoordinator InitializeCoordinatorWithoutProxy(MultiRunJob job)
    {
        var executionHandler = ExecutionHandlerFactory.CreateHandler(job.Config.Mode);
        return new BotExecutionCoordinator(executionHandler, null);
    }

    private static async Task<(IScript? Script, MethodInfo? DllMethod)> InitializeScriptAsync(
        Config config,
        PluginRepository pluginRepo,
        CancellationToken cancellationToken)
    {
        if (config.Mode is ConfigMode.LoliCode or ConfigMode.Stack)
        {
            config.StartupCSharpScript =
                Loli2CSharpTranspiler.Transpile(config.StartupLoliCodeScript, config.Settings);
        }

        if (config.Mode == ConfigMode.DLL)
        {
            await using var ms = new MemoryStream(config.DLLBytes);
            var assembly = AssemblyLoadContext.Default.LoadFromStream(ms);
            var type = assembly.GetType("RuriLib.CompiledConfig");
            var dllMethod = type?.GetMember("Execute").FirstOrDefault() as MethodInfo;
            return (null, dllMethod);
        }

        switch (config.Mode)
        {
            case ConfigMode.Stack:
                config.CSharpScript = Stack2CSharpTranspiler.Transpile(config.Stack, config.Settings);
                break;
            case ConfigMode.LoliCode:
                config.CSharpScript = Loli2CSharpTranspiler.Transpile(config.LoliCodeScript, config.Settings);
                break;
        }

        var script = new ScriptBuilder().Build(
            config.CSharpScript,
            config.Settings.ScriptSettings,
            pluginRepo,
            OptimizationLevel.Release);
        var diagnostics = script.Compile(cancellationToken);

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            var errors = string.Join(
                global::System.Environment.NewLine,
                diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.GetMessage()));

            throw new CompilationErrorException(
                "The C# script has compilation errors:" + global::System.Environment.NewLine + errors,
                diagnostics);
        }

        return (script, null);
    }

    private static async Task InitializeGlobalsAsync(
        MultiRunJob job,
        RuriLibSettingsService settings,
        PluginRepository pluginRepo,
        BotRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(job.Config.StartupCSharpScript))
        {
            var wordlistType = settings.Environment.WordlistTypes.FirstOrDefault(t => t.Name == job.DataPool.WordlistType);
            await ExecuteStartupScriptAsync(
                job,
                pluginRepo,
                runtimeContext,
                wordlistType,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ExecuteStartupScriptAsync(
        MultiRunJob job,
        PluginRepository pluginRepo,
        BotRuntimeContext runtimeContext,
        dynamic wordlistType,
        CancellationToken cancellationToken)
    {
        var startupScript = new ScriptBuilder().Build(
            job.Config.StartupCSharpScript,
            job.Config.Settings.ScriptSettings,
            pluginRepo,
            OptimizationLevel.Release);

        var diagnostics = startupScript.Compile(cancellationToken);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            var errors = string.Join(
                global::System.Environment.NewLine,
                diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.GetMessage()));

            throw new CompilationErrorException(
                "The Startup C# script has compilation errors:" + global::System.Environment.NewLine + errors,
                diagnostics);
        }

        var startupBotData = BotRuntimeContextBuilder.CreateBotData(new BotRuntimeSessionOptions
        {
            Providers = job.Providers,
            ConfigSettings = job.Config.Settings,
            Logger = new BotLogger(),
            Line = new DataLine(string.Empty, wordlistType),
            CancellationToken = cancellationToken,
            AsyncLocker = runtimeContext.AsyncLocker,
            SharedHttpClient = runtimeContext.HttpClient
        });

        _ = await BotRuntimeContextBuilder
            .ExecuteStartupScriptAsync(startupScript, startupBotData, runtimeContext.GlobalVariables, cancellationToken)
            .ConfigureAwait(false);
    }
}
