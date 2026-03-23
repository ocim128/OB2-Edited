using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flux.Core.Entities;
using Flux.Core.Models.Hits;
using Flux.Core.Models.Jobs;
using Flux.Core.Models.Proxies.Sources;
using Flux.Core.Repositories;
using Flux.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RuriLib.Extensions;
using RuriLib.Models.Data.DataPools;
using RuriLib.Models.Hits;
using RuriLib.Models.Hits.HitOutputs;
using RuriLib.Models.Jobs;
using RuriLib.Models.Jobs.Status;
using RuriLib.Models.Jobs.StartConditions;
using RuriLib.Models.Proxies.ProxySources;

namespace Flux.Shared.Services;

public class JobProjectionService(IServiceScopeFactory scopeFactory)
{
    public DesktopJobListItemDto ToDesktopListItem(Job job)
        => job switch
        {
            MultiRunJob multiRun => new DesktopJobListItemDto(
                multiRun.Id,
                JobType.MultiRun,
                multiRun.Status,
                multiRun.Name,
                multiRun.Config?.Metadata?.Name ?? "Config Missing",
                "Multi-Run Job",
                DescribeDataPool(multiRun),
                DescribeDataPoolDisplay(multiRun),
                multiRun.Bots,
                multiRun.Skip,
                multiRun.ProxyMode,
                multiRun.CPM,
                Math.Clamp(multiRun.Progress, 0, 1),
                multiRun.Status == JobStatus.Idle ? multiRun.Skip : multiRun.DataTested + multiRun.Skip,
                multiRun.DataPool?.Size ?? 0,
                multiRun.DataHits,
                multiRun.DataCustom,
                multiRun.StartTime,
                multiRun.Elapsed,
                multiRun.Remaining,
                multiRun.CpmTriggerEnabled),
            ProxyCheckJob proxyJob => new DesktopJobListItemDto(
                proxyJob.Id,
                JobType.ProxyCheck,
                proxyJob.Status,
                proxyJob.Name,
                "Proxy Check",
                "Proxy Check Job",
                $"URL: {proxyJob.Url}",
                proxyJob.Url,
                proxyJob.Bots,
                0,
                JobProxyMode.Off,
                proxyJob.CPM,
                Math.Clamp(proxyJob.Progress, 0, 1),
                proxyJob.Tested,
                proxyJob.Total,
                proxyJob.Working,
                0,
                proxyJob.StartTime,
                proxyJob.Elapsed,
                proxyJob.Remaining,
                false),
            _ => throw new NotImplementedException($"Unsupported job type {job.GetType().Name}")
        };

    public IReadOnlyList<DesktopJobListItemDto> BuildDesktopListItems(IEnumerable<Job> jobs)
        => jobs.Select(ToDesktopListItem).ToList();

    public JobSummaryDto ToSummary(Job job)
    {
        var progress = job switch
        {
            MultiRunJob multiRun => Math.Clamp(multiRun.Progress, 0, 1),
            ProxyCheckJob proxyJob => Math.Clamp(proxyJob.Progress, 0, 1),
            _ => 0
        };

        var bots = job switch
        {
            MultiRunJob multiRun => multiRun.Bots,
            ProxyCheckJob proxyJob => proxyJob.Bots,
            _ => 0
        };

        return new JobSummaryDto(
            job.Id,
            job.Name,
            (job as MultiRunJob)?.Config?.Metadata?.Name ?? job.GetType().Name,
            job.OwnerId == 0 ? "Admin" : job.OwnerId.ToString(),
            job.Status.ToString(),
            bots,
            progress,
            job.CreationTime,
            DateTime.UtcNow);
    }

    public IReadOnlyList<JobSummaryDto> BuildSummaries(IEnumerable<Job> jobs)
        => jobs.Select(ToSummary).ToList();

    public MultiRunJobViewerSnapshotDto? BuildMultiRunViewerSnapshot(Job? job, IReadOnlyDictionary<int, string> proxyGroupNames)
    {
        if (job is not MultiRunJob multiRun)
        {
            return null;
        }

        var config = multiRun.Config;
        var customInputs = config?.Settings?.InputSettings?.CustomInputs?
            .Select(input => new CustomInputPromptDto(input.VariableName, input.Description, input.DefaultAnswer))
            .ToList() ?? [];

        var customInputAnswers = multiRun.CustomInputsAnswers?.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value)
            ?? new Dictionary<string, string>();

        return new MultiRunJobViewerSnapshotDto(
            ToDesktopListItem(multiRun),
            config is not null,
            config?.Metadata?.Name ?? "No config",
            config is not null ? $"by {config.Metadata.Author}" : string.Empty,
            config?.Metadata?.Base64Image ?? string.Empty,
            DescribeDataPool(multiRun),
            FormatProxySourcesInfo(multiRun, proxyGroupNames),
            FormatHitOutputsInfo(multiRun),
            customInputs,
            customInputAnswers,
            GetWaitUntil(multiRun),
            multiRun.DataToCheck,
            multiRun.DataFails,
            multiRun.DataRetried,
            multiRun.DataBanned,
            multiRun.DataErrors,
            multiRun.DataInvalid,
            multiRun.ProxiesTotal,
            multiRun.ProxiesAlive,
            multiRun.ProxiesBad,
            multiRun.ProxiesBanned,
            multiRun.CaptchaCredit,
            BuildBotStates(multiRun),
            multiRun.Hits.Select(ToRuntimeResult).ToList());
    }

    public JobQueueDto BuildQueueSnapshot(IEnumerable<Job> jobs)
    {
        var running = new List<JobSummaryDto>();
        var waiting = new List<JobSummaryDto>();
        var idle = new List<JobSummaryDto>();
        var paused = new List<JobSummaryDto>();
        var completed = new List<JobSummaryDto>();

        foreach (var job in jobs)
        {
            var summary = ToSummary(job);
            switch (job.Status)
            {
                case JobStatus.Running:
                    running.Add(summary);
                    break;
                case JobStatus.Waiting:
                case JobStatus.Starting:
                    waiting.Add(summary);
                    break;
                case JobStatus.Paused:
                case JobStatus.Pausing:
                    paused.Add(summary);
                    break;
                case JobStatus.Stopping:
                    completed.Add(summary);
                    break;
                default:
                    idle.Add(summary);
                    break;
            }
        }

        return new JobQueueDto(running, waiting, idle, paused, completed);
    }

    public async Task<IReadOnlyList<JobResultDto>> GetRecentResultsAsync(Job? job, int take, CancellationToken cancellationToken = default)
    {
        if (job is null)
        {
            return Array.Empty<JobResultDto>();
        }

        using var scope = scopeFactory.CreateScope();
        var hitRepo = scope.ServiceProvider.GetRequiredService<IHitRepository>();
        var query = hitRepo.GetAll();

        if (job is MultiRunJob multiRun)
        {
            query = FilterHitsForJob(query, multiRun);
        }

        var entities = await query
            .OrderByDescending(h => h.Date)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(ToDto).ToList();
    }

    public async Task<JobDetailDto?> BuildDetailAsync(Job? job, CancellationToken cancellationToken = default)
    {
        if (job is null)
        {
            return null;
        }

        var results = await GetRecentResultsAsync(job, 50, cancellationToken).ConfigureAwait(false);
        var bots = BuildBotStates(job);
        var counters = BuildCounters(job);
        var dataPool = DescribeDataPool(job);

        return new JobDetailDto(
            ToSummary(job),
            dataPool,
            results,
            counters,
            bots,
            Array.Empty<NotificationDto>());
    }

    private static IReadOnlyList<BotStateDto> BuildBotStates(Job job)
    {
        if (job is not MultiRunJob multiRun || multiRun.CurrentBotDatas is null)
        {
            return Array.Empty<BotStateDto>();
        }

        var list = new List<BotStateDto>(multiRun.CurrentBotDatas.Length);
        for (var i = 0; i < multiRun.CurrentBotDatas.Length; i++)
        {
            var data = multiRun.CurrentBotDatas[i];
            if (data is null)
            {
                continue;
            }

            list.Add(new BotStateDto(
                i + 1,
                data.Line?.Data,
                data.Proxy?.ToString(),
                data.ExecutionInfo));
        }

        return list;
    }

    public BotLogDto? BuildBotLog(Job? job, string resultId)
    {
        if (job is not MultiRunJob multiRun)
        {
            return null;
        }

        var hit = multiRun.Hits.FirstOrDefault(h => h?.Id == resultId);
        if (hit is null)
        {
            return null;
        }

        var entries = hit.BotLogger?.Entries?
            .Select(entry => new BotLogEntryDto(entry.Message, entry.Color))
            .ToList() ?? [];

        return new BotLogDto(entries);
    }

    private static JobCountersDto BuildCounters(Job job)
    {
        if (job is MultiRunJob multiRun)
        {
            var stats = multiRun.Statistics;
            return new JobCountersDto(
                stats.Hits,
                stats.Custom,
                stats.ToCheck,
                stats.Fails,
                multiRun.Bots,
                multiRun.CPM,
                Math.Clamp(multiRun.Progress, 0, 1));
        }

        if (job is ProxyCheckJob proxyJob)
        {
            return new JobCountersDto(
                proxyJob.Working,
                0,
                0,
                proxyJob.NotWorking,
                proxyJob.Bots,
                proxyJob.CPM,
                Math.Clamp(proxyJob.Progress, 0, 1));
        }

        return new JobCountersDto(0, 0, 0, 0, 0, 0, 0);
    }

    private static string DescribeDataPool(Job job)
    {
        if (job is not MultiRunJob multiRun || multiRun.DataPool is null)
        {
            return "Unknown";
        }

        return multiRun.DataPool switch
        {
            WordlistDataPool w => $"Wordlist: {w.Wordlist?.Name ?? "Unknown"}",
            FileDataPool f => $"File: {f.FileName}",
            RangeDataPool r => $"Range: {r.Start}-{r.Amount}",
            CombinationsDataPool c => $"Combinations: {c.CharSet} x {c.Length}",
            InfiniteDataPool => "Infinite",
            _ => multiRun.DataPool.GetType().Name
        };
    }

    private static string DescribeDataPoolDisplay(MultiRunJob job)
        => job.DataPool switch
        {
            WordlistDataPool w => w.Wordlist?.Name ?? "Wordlist",
            CombinationsDataPool => "Combinations",
            InfiniteDataPool => "Infinite",
            RangeDataPool => "Range",
            FileDataPool f => System.IO.Path.GetFileName(f.FileName),
            null => "Unknown",
            _ => job.DataPool.GetType().Name
        };

    private static JobResultDto ToDto(HitEntity entity)
        => new(entity.OwnerId, entity.Type, entity.Data, entity.CapturedData, entity.Proxy, entity.Date);

    private static JobRuntimeResultDto ToRuntimeResult(Hit hit)
        => new(
            hit.Id,
            hit.Type ?? string.Empty,
            hit.Data?.Data ?? string.Empty,
            hit.CapturedDataString ?? string.Empty,
            hit.Proxy?.ToString() ?? string.Empty,
            hit.Proxy?.Type,
            hit.Date,
            hit.Config?.Mode,
            hit.BotLogger is not null);

    private static string FormatProxySourcesInfo(MultiRunJob job, IReadOnlyDictionary<int, string> proxyGroupNames)
    {
        if (job.ProxySources is null || job.ProxySources.Count == 0)
        {
            return "None";
        }

        return string.Join(" | ", job.ProxySources.Select(source => source switch
        {
            GroupProxySource g => $"Group ({GetProxyGroupName(proxyGroupNames, g.GroupId)})",
            FileProxySource f => $"File ({f.FileName})",
            RemoteProxySource r => $"Remote ({r.Url})",
            _ => source.GetType().Name
        }));
    }

    private static string FormatHitOutputsInfo(MultiRunJob job)
    {
        if (job.HitOutputs is null || job.HitOutputs.Count == 0)
        {
            return "None";
        }

        return string.Join(" | ", job.HitOutputs.Select(output => output switch
        {
            DatabaseHitOutput => "Database",
            FileSystemHitOutput fs => $"File System ({fs.BaseDir})",
            DiscordWebhookHitOutput d => $"Discord ({d.Webhook.TruncatePretty(70)})",
            TelegramBotHitOutput t => $"Telegram ({t.Token.Split(':')[0]})",
            CustomWebhookHitOutput c => $"Custom Webhook ({c.Url.TruncatePretty(70)})",
            _ => output.GetType().Name
        }));
    }

    private static string GetProxyGroupName(IReadOnlyDictionary<int, string> proxyGroupNames, int id)
        => proxyGroupNames.TryGetValue(id, out var name) ? name : "Invalid";

    private static DateTime? GetWaitUntil(MultiRunJob job)
        => job.StartCondition switch
        {
            RelativeTimeStartCondition relative => job.StartTime + relative.StartAfter,
            AbsoluteTimeStartCondition absolute => absolute.StartAt,
            _ => null
        };

    private static IQueryable<HitEntity> FilterHitsForJob(IQueryable<HitEntity> query, MultiRunJob job)
    {
        var configId = job.Config?.Id;
        query = !string.IsNullOrWhiteSpace(configId)
            ? query.Where(h => h.ConfigId == configId)
            : query;

        if (job.DataPool is WordlistDataPool wordlist)
        {
            var id = wordlist.Wordlist?.Id ?? -1;
            query = query.Where(h => h.WordlistId == id);
        }

        return query;
    }
}
