using RuriLib.Models.Bots;
using RuriLib.Models.Configs;
using RuriLib.Models.Data;
using RuriLib.Logging;
using System;
using System.Collections.Generic;

namespace RuriLib.Models.Jobs;

internal sealed class WorkItemFactory
{
    private readonly BotSessionFactory _botSessionFactory = new();

    public IEnumerable<MultiRunInput> Create(MultiRunJob job, dynamic wordlistType)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(job.ResourceScope);

        var useProxies = MultiRunJob.ShouldUseProxies(job.ProxyMode, job.Config.Settings.ProxySettings);
        var isDll = job.Config.Mode == ConfigMode.DLL;
        var configSettings = job.Config.Settings;
        var customAnswers = job.CustomInputsAnswers;
        var scope = job.ResourceScope;

        long index = 0;

        foreach (var line in job.DataPool.DataList)
        {
            yield return new MultiRunInput
            {
                Job = job,
                ProxyPool = scope.ProxyPool,
                BotData = _botSessionFactory.CreateBotData(
                    job.Providers,
                    configSettings,
                    new BotLogger(),
                    new DataLine(line, wordlistType),
                    useProxy: useProxies,
                    asyncLocker: scope.AsyncLocker,
                    sharedHttpClient: scope.HttpClient),
                Globals = scope.GlobalVariables,
                Script = scope.Script,
                IsDLL = isDll,
                DLLMethod = scope.DllMethod,
                CustomInputsAnswers = customAnswers,
                Index = index++
            };
        }
    }
}
