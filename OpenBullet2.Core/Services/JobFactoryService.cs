using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenBullet2.Core.Models.Hits;
using OpenBullet2.Core.Models.Jobs;
using OpenBullet2.Core.Models.Proxies;
using OpenBullet2.Core.Repositories;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Jobs;
using RuriLib.Models.Proxies;
using RuriLib.Providers.RandomNumbers;
using RuriLib.Providers.UserAgents;
using RuriLib.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OpenBullet2.Core.Services;

/// <summary>
/// Factory that creates a <see cref="Job"/> from <see cref="JobOptions"/>.
/// </summary>
public class JobFactoryService
{
    private readonly ConfigService _configService;
    private readonly RuriLibSettingsService _settingsService;
    private readonly HitStorageService _hitStorage;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProxyCheckOutputFactory _proxyCheckOutputFactory;
    private readonly ProxyReloadService _proxyReloadService;
    private readonly IRandomUAProvider _randomUaProvider;
    private readonly IRNGProvider _rngProvider;
    private readonly IJobLogger _logger;
    private readonly PluginRepository _pluginRepo;

    /// <summary>
    /// The maximum amount of bots that a job can use.
    /// </summary>
    public int BotLimit { get; init; } = 200;

    public JobFactoryService(ConfigService configService, RuriLibSettingsService settingsService, PluginRepository pluginRepo,
        HitStorageService hitStorage, IServiceScopeFactory scopeFactory, ProxyCheckOutputFactory proxyCheckOutputFactory,
        ProxyReloadService proxyReloadService, IRandomUAProvider randomUaProvider, IRNGProvider rngProvider, IJobLogger logger,
        IConfiguration config)
    {
        _configService = configService;
        _settingsService = settingsService;
        _pluginRepo = pluginRepo;
        _hitStorage = hitStorage;
        _scopeFactory = scopeFactory;
        _proxyCheckOutputFactory = proxyCheckOutputFactory;
        _proxyReloadService = proxyReloadService;
        _randomUaProvider = randomUaProvider;
        _rngProvider = rngProvider;
        _logger = logger;

        var botLimit = config.GetSection("Resources")["BotLimit"];

        if (botLimit is not null)
        {
            BotLimit = int.Parse(botLimit);
        }
    }

    /// <summary>
    /// Creates a <see cref="Job"/> with the provided <paramref name="id"/> and <paramref name="ownerId"/>
    /// from <see cref="JobOptions"/>.
    /// </summary>
    /// <param name="id">The ID of the newly created job, must be unique</param>
    /// <param name="ownerId">The ID of the user who owns the job. 0 for admin</param>
    /// <param name="options">The options to create the job from</param>
    public async Task<Job> FromOptionsAsync(int id, int ownerId, JobOptions options)
    {
        Job job = options switch
        {
            MultiRunJobOptions x => await MakeMultiRunJobAsync(x).ConfigureAwait(false),
            ProxyCheckJobOptions x => await MakeProxyCheckJobAsync(x).ConfigureAwait(false),
            _ => throw new NotImplementedException()
        };

        job.Id = id;
        job.OwnerId = ownerId;
        return job;
    }

    private async Task<MultiRunJob> MakeMultiRunJobAsync(MultiRunJobOptions options)
    {
        using var scope = _scopeFactory.CreateScope();
        var proxySourceFactory = scope.ServiceProvider.GetRequiredService<ProxySourceFactoryService>();
        var dataPoolFactory = scope.ServiceProvider.GetRequiredService<DataPoolFactoryService>();
        var configRepository = scope.ServiceProvider.GetRequiredService<IConfigRepository>();

        var hitOutputsFactory = new HitOutputFactory(_hitStorage);

        var config = default(RuriLib.Models.Configs.Config);

        if (!string.IsNullOrWhiteSpace(options.ConfigId))
        {
            config = _configService.GetConfigsList().FirstOrDefault(c => c.Id == options.ConfigId);

            if (config is null)
            {
                try
                {
                    config = await configRepository.GetAsync(options.ConfigId).ConfigureAwait(false);

                    if (config is not null && !_configService.GetConfigsList().Any(c => c.Id == config.Id))
                    {
                        _configService.AddConfig(config);
                    }
                }
                catch
                {
                    // If the config cannot be loaded, we will fall back to a placeholder below.
                }
            }
        }

        // Parallelize async operations where appropriate to speed up initialization
        var proxySourcesTask = Task.WhenAll(options.ProxySources
            .Select(s => proxySourceFactory.FromOptions(s)));
            
        var dataPoolTask = dataPoolFactory.FromOptionsAsync(options.DataPool);

        await Task.WhenAll(proxySourcesTask, dataPoolTask).ConfigureAwait(false);

        var job = new MultiRunJob(_settingsService, _pluginRepo, _logger)
        {
            Config = config ?? new RuriLib.Models.Configs.Config
            {
                Id = string.IsNullOrWhiteSpace(options.ConfigId) ? "missing" : options.ConfigId,
                Metadata = new RuriLib.Models.Configs.ConfigMetadata { Name = "Config Missing" }
            },
            CreationTime = DateTime.Now,
            ProxyMode = options.ProxyMode,
            ShuffleProxies = options.ShuffleProxies,
            NoValidProxyBehaviour = options.NoValidProxyBehaviour,
            NeverBanProxies = options.NeverBanProxies,
            MarkAsToCheckOnAbort = options.MarkAsToCheckOnAbort,
            ProxyBanTime = TimeSpan.FromSeconds(options.ProxyBanTimeSeconds),
            ConcurrentProxyMode = options.ConcurrentProxyMode,
            CpmTriggerEnabled = options.CpmTriggerEnabled,
            PeriodicReloadInterval = TimeSpan.FromSeconds(options.PeriodicReloadIntervalSeconds),
            StartCondition = options.StartCondition,
            Name = options.Name,
            Bots = options.Bots,
            BotLimit = BotLimit,
            CurrentBotDatas = new BotData[BotLimit],
            Skip = options.Skip,
            HitOutputs = options.HitOutputs.Select(o => hitOutputsFactory.FromOptions(o)).ToList(),
            ProxySources = (await proxySourcesTask).ToList(),
            Providers = new(_settingsService)
            {
                RandomUA = _settingsService.RuriLibSettings.GeneralSettings.UseCustomUserAgentsList
                    ? new DefaultRandomUAProvider(_settingsService)
                    : _randomUaProvider,
                RNG = _rngProvider
            },
            DataPool = await dataPoolTask
        };

        return job;
    }

    private async Task<ProxyCheckJob> MakeProxyCheckJobAsync(ProxyCheckJobOptions options)
    {
        var job = new ProxyCheckJob(_settingsService, _pluginRepo, _logger)
        {
            StartCondition = options.StartCondition,
            Bots = options.Bots,
            Name = options.Name,
            BotLimit = BotLimit,
            CheckOnlyUntested = options.CheckOnlyUntested,
            Url = options.Target.Url,
            SuccessKey = options.Target.SuccessKey,
            Timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds),
            GeoProvider = new DBIPProxyGeolocationProvider("dbip-country-lite.mmdb")
        };

        job.Proxies = await _proxyReloadService.ReloadAsync(options.GroupId, job.OwnerId).ConfigureAwait(false);

        // Update the stats
        var proxies = options.CheckOnlyUntested
            ? job.Proxies.Where(p => p.WorkingStatus == ProxyWorkingStatus.Untested)
            : job.Proxies;

        var proxiesList = proxies.ToList();
        job.Total = proxiesList.Count;
        job.Tested = proxiesList.Count(p => p.WorkingStatus != ProxyWorkingStatus.Untested);
        job.Working = proxiesList.Count(p => p.WorkingStatus == ProxyWorkingStatus.Working);
        job.NotWorking = proxiesList.Count(p => p.WorkingStatus == ProxyWorkingStatus.NotWorking);
        job.ProxyOutput = _proxyCheckOutputFactory.FromOptions(new DatabaseProxyCheckOutputOptions());

        return job;
    }
}
