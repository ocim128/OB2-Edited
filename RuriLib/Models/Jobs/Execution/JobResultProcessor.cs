using RuriLib.Models.Configs;
using RuriLib.Models.Hits;
using RuriLib.Models.Jobs.Statistics;
using RuriLib.Models.Jobs.Status;
using RuriLib.Models.Proxies;
using RuriLib.Parallelization;
using RuriLib.Parallelization.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RuriLib.Models.Jobs;

internal sealed class JobResultProcessor
{
    private readonly MultiRunJob job;
    private readonly ConcurrentQueue<Hit> hits = new();

    public JobResultProcessor(MultiRunJob job)
    {
        this.job = job;
    }

    public IReadOnlyCollection<Hit> Hits => hits;

    public JobStatistics Statistics { get; } = new();

    public void Reset()
    {
        Statistics.Reset();
        hits.Clear();
    }

    public void HandleParallelizerResult(object _, ResultDetails<MultiRunInput, CheckResult> result)
    {
        job.RaiseResult(result);

        if (!job.ShouldLogAllResults)
        {
            return;
        }

        var data = result.Result.BotData;
        job.LogInfo($"[{data.STATUS}] {data.Line.Data} ({data.Proxy})");
    }

    public void HandleDataProcessed(object sender, ResultDetails<MultiRunInput, CheckResult> details)
    {
        var botData = details.Result.BotData;

        if (BotStatus.IsHitStatus(botData.STATUS))
        {
            _ = RegisterHitAsync(details.Result).ConfigureAwait(false);
        }

        Statistics.UpdateForStatus(botData.STATUS);

        if (job.Parallelizer?.Status == ParallelizerStatus.Stopping)
        {
            details.Item.BotData.ExecutionInfo = "STOPPED";
        }
    }

    private async Task RegisterHitAsync(CheckResult result)
    {
        var botData = result.BotData;

        var hit = new Hit
        {
            Data = botData.Line,
            BotLogger = job.ShouldPersistBotLogForHits
                ? botData.Logger
                : null,
            Type = botData.STATUS,
            DataPool = job.DataPool,
            Config = job.Config,
            Date = DateTime.Now,
            Proxy = botData.Proxy,
            CapturedData = job.Config.Settings.GeneralSettings.SaveEmptyCaptures
                ? result.OutputVariables
                : CleanEmptyCaptures(result.OutputVariables),
            OwnerId = job.OwnerId
        };

        hits.Enqueue(hit);
        job.RaiseHit(hit);

        foreach (var hitOutput in job.HitOutputs)
        {
            await hitOutput.Store(hit).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, object> CleanEmptyCaptures(Dictionary<string, object> capturedData)
    {
        var newCaptures = new Dictionary<string, object>(capturedData.Count);

        foreach (var kvp in capturedData)
        {
            var value = kvp.Value;
            if (value is string s && string.IsNullOrWhiteSpace(s))
            {
                continue;
            }

            if (value is byte[] b && b.Length == 0)
            {
                continue;
            }

            if (value is List<string> l && l.Count == 0)
            {
                continue;
            }

            if (value is Dictionary<string, string> d && d.Count == 0)
            {
                continue;
            }

            newCaptures[kvp.Key] = value;
        }

        return newCaptures;
    }
}
