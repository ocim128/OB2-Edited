using RuriLib.Helpers;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs;
using RuriLib.Models.Data;
using RuriLib.Models.Jobs.Execution;
using RuriLib.Models.Proxies;
using RuriLib.Models.Scripting;
using RuriLib.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Models.Jobs;

internal sealed class JobInitializer
{
    private readonly ScriptPreparationService _scriptPreparation = new();
    private readonly BotSessionFactory _botSessionFactory = new();

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

        var runtimeContext = _botSessionFactory.CreateRuntimeContext(
            job.Config.Settings.DataSettings.Resources,
            job.OwnerId,
            job.Id,
            includePythonEngine: true,
            asyncLocker: asyncLocker);
        var scriptState = await _scriptPreparation
            .PrepareAsync(job.Config, pluginRepo, cancellationToken)
            .ConfigureAwait(false);
        await InitializeGlobalsAsync(
            job,
            settings,
            runtimeContext,
            scriptState,
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

    private async Task InitializeGlobalsAsync(
        MultiRunJob job,
        RuriLibSettingsService settings,
        BotRuntimeContext runtimeContext,
        ScriptPreparationResult scriptState,
        CancellationToken cancellationToken)
    {
        if (scriptState.StartupScript is not null)
        {
            var wordlistType = settings.Environment.WordlistTypes.FirstOrDefault(t => t.Name == job.DataPool.WordlistType);
            await ExecuteStartupScriptAsync(
                job,
                runtimeContext,
                scriptState.StartupScript,
                wordlistType,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteStartupScriptAsync(
        MultiRunJob job,
        BotRuntimeContext runtimeContext,
        IScript startupScript,
        dynamic wordlistType,
        CancellationToken cancellationToken)
    {
        var startupBotData = _botSessionFactory.CreateStartupBotData(
            job.Providers,
            job.Config.Settings,
            new BotLogger(),
            wordlistType,
            cancellationToken,
            asyncLocker: runtimeContext.AsyncLocker,
            sharedHttpClient: runtimeContext.HttpClient);

        await _scriptPreparation
            .ExecuteStartupScriptAsync(startupScript, startupBotData, runtimeContext.GlobalVariables, cancellationToken)
            .ConfigureAwait(false);
    }
}
