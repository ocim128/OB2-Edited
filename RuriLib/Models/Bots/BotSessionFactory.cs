using RuriLib.Logging;
using RuriLib.Helpers;
using RuriLib.Models.Configs;
using RuriLib.Models.Data;
using RuriLib.Models.Data.Resources.Options;
using RuriLib.Models.Proxies;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;

namespace RuriLib.Models.Bots;

internal sealed class BotSessionFactory
{
    public BotRuntimeContext CreateRuntimeContext(
        IReadOnlyList<ConfigResourceOptions> resourceOptions,
        int ownerId,
        int jobId,
        bool includePythonEngine,
        AsyncLocker? asyncLocker = null,
        IBotLogger? logger = null,
        bool continueOnResourceError = false)
        => BotRuntimeContextBuilder.CreateContext(
            resourceOptions,
            ownerId,
            jobId,
            includePythonEngine,
            asyncLocker,
            logger,
            continueOnResourceError);

    public BotData CreateBotData(
        Providers providers,
        ConfigSettings configSettings,
        IBotLogger logger,
        DataLine line,
        Proxy? proxy = null,
        bool useProxy = false,
        CancellationToken cancellationToken = default,
        Stepper? stepper = null,
        AsyncLocker? asyncLocker = null,
        HttpClient? sharedHttpClient = null)
        => BotRuntimeContextBuilder.CreateBotData(new BotRuntimeSessionOptions
        {
            Providers = providers,
            ConfigSettings = configSettings,
            Logger = logger,
            Line = line,
            Proxy = proxy,
            UseProxy = useProxy,
            CancellationToken = cancellationToken,
            Stepper = stepper,
            AsyncLocker = asyncLocker,
            SharedHttpClient = sharedHttpClient
        });

    public BotData CreateStartupBotData(
        Providers providers,
        ConfigSettings configSettings,
        IBotLogger logger,
        dynamic wordlistType,
        CancellationToken cancellationToken = default,
        Stepper? stepper = null,
        AsyncLocker? asyncLocker = null,
        HttpClient? sharedHttpClient = null)
        => CreateBotData(
            providers,
            configSettings,
            logger,
            new DataLine(string.Empty, wordlistType),
            cancellationToken: cancellationToken,
            stepper: stepper,
            asyncLocker: asyncLocker,
            sharedHttpClient: sharedHttpClient);
}
