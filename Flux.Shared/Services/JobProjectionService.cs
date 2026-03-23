using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flux.Core.Entities;
using Flux.Core.Repositories;
using Flux.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RuriLib.Models.Data.DataPools;
using RuriLib.Models.Jobs;
using RuriLib.Models.Jobs.Status;

namespace Flux.Shared.Services;

public class JobProjectionService(IServiceScopeFactory scopeFactory)
{
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

    private static JobResultDto ToDto(HitEntity entity)
        => new(entity.OwnerId, entity.Type, entity.Data, entity.CapturedData, entity.Proxy, entity.Date);

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
