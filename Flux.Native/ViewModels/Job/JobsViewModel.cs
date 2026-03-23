using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Flux.Core.Models.Jobs;
using Flux.Native.Services;
using Flux.Native.ViewModels.Base;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;
using RuriLib.Models.Jobs;

namespace Flux.Native.ViewModels.Jobs;

public class JobsViewModel : ViewModelBase
{
    private readonly IJobQueries jobQueries;
    private readonly IJobOrchestrator jobOrchestrator;
    private readonly HotkeyService hotkeyService;
    private readonly Timer timer;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly object cpmTriggerLock = new();
    private readonly Dictionary<int, CpmTriggerState> cpmTriggerStates = new();

    private List<DesktopJobListItemDto> allJobs = [];

    private ObservableCollection<JobViewModel> jobsCollection = [];
    public ObservableCollection<JobViewModel> JobsCollection
    {
        get => jobsCollection;
        set
        {
            jobsCollection = value;
            OnPropertyChanged();
        }
    }

    private string searchText = string.Empty;
    public string SearchText
    {
        get => searchText;
        set
        {
            searchText = value;
            OnPropertyChanged();
            FilterJobs();
        }
    }

    public JobsViewModel(
        IJobQueries jobQueries,
        IJobOrchestrator jobOrchestrator,
        HotkeyService hotkeyService)
    {
        this.jobQueries = jobQueries ?? throw new ArgumentNullException(nameof(jobQueries));
        this.jobOrchestrator = jobOrchestrator ?? throw new ArgumentNullException(nameof(jobOrchestrator));
        this.hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));

        RefreshJobsAsync().GetAwaiter().GetResult();
        timer = new Timer(_ => _ = RefreshJobsAsync(), null, 2000, 2000);
    }

    public async Task<JobViewModel> CreateJobAsync(JobOptions options)
    {
        var jobId = await jobOrchestrator.CreateJobAsync(options).ConfigureAwait(false);
        await RefreshJobsAsync().ConfigureAwait(false);
        return JobsCollection.First(job => job.Id == jobId);
    }

    public Task<JobOptionsSnapshotDto?> GetJobOptionsAsync(int jobId, bool clone = false)
        => jobOrchestrator.GetJobOptionsAsync(jobId, clone);

    public async Task<JobViewModel> EditJobAsync(int jobId, JobOptions options)
    {
        var updatedJobId = await jobOrchestrator.UpdateJobAsync(jobId, options).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Job {jobId} could not be updated");

        await RefreshJobsAsync().ConfigureAwait(false);
        return JobsCollection.First(job => job.Id == updatedJobId);
    }

    public async Task RemoveAllAsync()
    {
        await jobOrchestrator.DeleteAllJobsAsync().ConfigureAwait(false);
        await RefreshJobsAsync().ConfigureAwait(false);
    }

    public async Task RemoveJobAsync(JobViewModel jobVM)
    {
        var deleted = await jobOrchestrator.DeleteJobAsync(jobVM.Id).ConfigureAwait(false);
        if (!deleted)
        {
            throw new InvalidOperationException($"Job {jobVM.Id} could not be deleted");
        }

        await RefreshJobsAsync().ConfigureAwait(false);
    }

    private async Task RefreshJobsAsync()
    {
        if (!await refreshLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var latestJobs = await jobQueries.GetDesktopJobsAsync().ConfigureAwait(false);
            allJobs = latestJobs.OrderBy(static job => job.Id).ToList();
            FilterJobs();
            EvaluateCpmTriggers(allJobs);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private void FilterJobs()
    {
        var filteredJobs = allJobs
            .Where(JobMatchesSearch)
            .Select(MakeViewModel)
            .OrderBy(static job => job.Id)
            .ToList();

        RunOnUiThread(() => JobsCollection = new ObservableCollection<JobViewModel>(filteredJobs));
    }

    private bool JobMatchesSearch(DesktopJobListItemDto job)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return job.ConfigDisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || job.DataPoolDisplayInfo.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || job.Id.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

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

    private static JobViewModel MakeViewModel(DesktopJobListItemDto snapshot) => snapshot.JobType switch
    {
        JobType.MultiRun => new MultiRunJobViewModel(snapshot),
        JobType.ProxyCheck => new ProxyCheckJobViewModel(snapshot),
        _ => throw new NotImplementedException($"Unsupported job type {snapshot.JobType}")
    };

    private static void RunOnUiThread(Action action)
    {
        if (Application.Current?.Dispatcher is null || Application.Current.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Application.Current.Dispatcher.Invoke(action);
    }

    private sealed class CpmTriggerState
    {
        public DateTime JobStartTime { get; set; }
        public DateTime? RunStartTime { get; set; }
        public DateTime NextAttemptAt { get; set; }
        public bool AttemptInProgress { get; set; }
    }
}
