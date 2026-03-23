using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Flux.Core.Models.Jobs;
using Flux.Shared.Models;
using RuriLib.Models.Jobs;

namespace Flux.Native.ViewModels.Jobs;

public partial class JobsViewModel
{
    private readonly object cpmTriggerLock = new();
    private readonly Dictionary<int, CpmTriggerState> cpmTriggerStates = new();

    private void EvaluateCpmTriggers(IEnumerable<DesktopJobListItemDto> jobs)
    {
        var now = DateTime.Now;
        var desktopJobs = jobs.ToList();
        var activeJobIds = desktopJobs.Select(job => job.Id).ToHashSet();

        foreach (var job in desktopJobs.Where(static job => job.JobType == JobType.MultiRun))
        {
            if (!job.CpmTriggerEnabled || job.Status == JobStatus.Idle)
            {
                RemoveCpmState(job.Id);
                continue;
            }

            var shouldAttempt = false;
            DateTime jobStartTime;

            lock (cpmTriggerLock)
            {
                var state = GetOrCreateCpmState(job, now);
                jobStartTime = state.JobStartTime;

                if (job.Status != JobStatus.Running)
                {
                    continue;
                }

                if (!state.RunStartTime.HasValue)
                {
                    var elapsed = job.Elapsed;
                    state.RunStartTime = elapsed > TimeSpan.Zero ? now - elapsed : now;
                    state.NextAttemptAt = state.RunStartTime.Value.AddMinutes(1);
                    continue;
                }

                if (state.AttemptInProgress || now < state.NextAttemptAt)
                {
                    continue;
                }

                if (now - state.RunStartTime.Value < TimeSpan.FromMinutes(1))
                {
                    state.NextAttemptAt = state.RunStartTime.Value.AddMinutes(1);
                    continue;
                }

                if (job.Cpm >= 5000)
                {
                    state.NextAttemptAt = now.AddSeconds(5);
                    continue;
                }

                state.AttemptInProgress = true;
                shouldAttempt = true;
            }

            if (shouldAttempt)
            {
                _ = AttemptCpmTriggerAsync(job.Id, jobStartTime);
            }
        }

        CleanupCpmStates(activeJobIds);
    }

    private CpmTriggerState GetOrCreateCpmState(DesktopJobListItemDto job, DateTime now)
    {
        if (!cpmTriggerStates.TryGetValue(job.Id, out var state))
        {
            state = new CpmTriggerState
            {
                JobStartTime = job.StartTime,
                NextAttemptAt = now.AddMinutes(1)
            };
            cpmTriggerStates[job.Id] = state;
            return state;
        }

        if (state.JobStartTime != job.StartTime)
        {
            state.JobStartTime = job.StartTime;
            state.RunStartTime = null;
            state.AttemptInProgress = false;
            state.NextAttemptAt = now.AddMinutes(1);
        }

        return state;
    }

    private async Task AttemptCpmTriggerAsync(int jobId, DateTime jobStartTime)
    {
        var success = await hotkeyService.TriggerModemRefreshAsync().ConfigureAwait(false);
        var nextDelay = success ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(5);

        lock (cpmTriggerLock)
        {
            if (cpmTriggerStates.TryGetValue(jobId, out var state) && state.JobStartTime == jobStartTime)
            {
                state.NextAttemptAt = DateTime.Now.Add(nextDelay);
                state.AttemptInProgress = false;
            }
        }
    }

    private void CleanupCpmStates(HashSet<int> activeJobIds)
    {
        lock (cpmTriggerLock)
        {
            var staleIds = cpmTriggerStates.Keys.Where(id => !activeJobIds.Contains(id)).ToList();
            foreach (var id in staleIds)
            {
                cpmTriggerStates.Remove(id);
            }
        }
    }

    private void RemoveCpmState(int jobId)
    {
        lock (cpmTriggerLock)
        {
            cpmTriggerStates.Remove(jobId);
        }
    }

    private sealed class CpmTriggerState
    {
        public DateTime JobStartTime { get; set; }
        public DateTime? RunStartTime { get; set; }
        public DateTime NextAttemptAt { get; set; }
        public bool AttemptInProgress { get; set; }
    }
}
