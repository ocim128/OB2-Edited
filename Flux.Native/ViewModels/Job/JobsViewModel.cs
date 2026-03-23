using Flux.Core.Models.Jobs;
using Flux.Core.Services;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;
using RuriLib.Models.Data.DataPools;
using RuriLib.Models.Jobs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

using Flux.Native.Services;
using Flux.Native.ViewModels.Base;

namespace Flux.Native.ViewModels.Jobs;

public class JobsViewModel : ViewModelBase
{
    private readonly JobManagerService jobManager;
    private readonly IJobOrchestrator jobOrchestrator;
    private readonly HotkeyService hotkeyService;
    private readonly Timer _timer;
    private readonly object cpmTriggerLock = new();
    private readonly Dictionary<int, CpmTriggerState> cpmTriggerStates = new();

    private ObservableCollection<JobViewModel> jobsCollection;
    public ObservableCollection<JobViewModel> JobsCollection
    {
        get => jobsCollection;
        set
        {
            jobsCollection = value;
            OnPropertyChanged();
        }
    }

    private string searchText = "";
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
        JobManagerService jobManagerService,
        IJobOrchestrator jobOrchestrator,
        HotkeyService hotkeyService)
    {
        jobManager = jobManagerService ?? throw new ArgumentNullException(nameof(jobManagerService));
        this.jobOrchestrator = jobOrchestrator ?? throw new ArgumentNullException(nameof(jobOrchestrator));
        this.hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));

        CreateCollection();
        _timer = new Timer(new TimerCallback(_ => RefreshJobs()), null, 2000, 2000);
    }

    private void FilterJobs()
    {
        var allJobs = jobManager.Jobs.Select(MakeViewModel);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            allJobs = allJobs.Where(job =>
            {
                if (job is MultiRunJobViewModel mrJob)
                {
                    return mrJob.ConfigDisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                           mrJob.DataPoolDisplayInfo.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                           mrJob.Id.ToString().Contains(SearchText);
                }
                else if (job is ProxyCheckJobViewModel pcJob)
                {
                    return pcJob.ConfigDisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                           pcJob.DataPoolDisplayInfo.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                           pcJob.Id.ToString().Contains(SearchText);
                }
                return job.Id.ToString().Contains(SearchText);
            });
        }

        JobsCollection = new ObservableCollection<JobViewModel>(allJobs);
        SortCollection();
    }

    private void RefreshJobs()
    {
        // Only refresh jobs that are not idle to reduce unnecessary updates
        foreach (var job in JobsCollection.Where(j => j.Status != JobStatus.Idle))
        {
            job.UpdateViewModel();
        }

        EvaluateCpmTriggers();
    }

    private void EvaluateCpmTriggers()
    {
        var now = DateTime.Now;
        var activeJobIds = jobManager.Jobs.Select(j => j.Id).ToHashSet();

        foreach (var job in jobManager.Jobs.OfType<MultiRunJob>())
        {
            if (!job.CpmTriggerEnabled || job.Status == JobStatus.Idle)
            {
                RemoveCpmState(job.Id);
                continue;
            }

            bool shouldAttempt = false;
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

                if (job.CPM >= 5000)
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

    private CpmTriggerState GetOrCreateCpmState(MultiRunJob job, DateTime now)
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

    private void CreateCollection() => FilterJobs();

    private void SortCollection()
        => JobsCollection = new ObservableCollection<JobViewModel>(JobsCollection.OrderBy(static j => j.Id));

    public async Task<JobViewModel> CreateJobAsync(JobOptions options)
    {
        var jobId = await jobOrchestrator.CreateJobAsync(options);
        CreateCollection();
        return JobsCollection.First(j => j.Id == jobId);
    }

    public Task<JobOptionsSnapshotDto?> GetJobOptionsAsync(int jobId, bool clone = false)
        => jobOrchestrator.GetJobOptionsAsync(jobId, clone);

    public async Task<JobViewModel> EditJobAsync(int jobId, JobOptions options)
    {
        var updatedJobId = await jobOrchestrator.UpdateJobAsync(jobId, options)
            ?? throw new InvalidOperationException($"Job {jobId} could not be updated");

        CreateCollection();
        return JobsCollection.First(j => j.Id == updatedJobId);
    }

    public async Task RemoveAllAsync()
    {
        await jobOrchestrator.DeleteAllJobsAsync();
        JobsCollection.Clear();
    }

    public async Task RemoveJobAsync(JobViewModel jobVM)
    {
        var deleted = await jobOrchestrator.DeleteJobAsync(jobVM.Id);
        if (!deleted)
        {
            throw new InvalidOperationException($"Job {jobVM.Id} could not be deleted");
        }

        _ = JobsCollection.Remove(jobVM);
        SortCollection();
    }

    private static JobViewModel MakeViewModel(Job job) => job switch
    {
        MultiRunJob mr => new MultiRunJobViewModel(mr),
        ProxyCheckJob pc => new ProxyCheckJobViewModel(pc),
        _ => throw new NotImplementedException()
    };

}

public class JobViewModel(Job job) : ViewModelBase
{
    public Job Job { get; init; } = job;

    public string IdAndStatus => $"#{Id} [{Status}]";
    public int Id => Job.Id;
    public JobStatus Status => Job.Status;

    /// <summary>
    /// UI Display Properties
    /// </summary>
    /// <exception cref="NotImplementedException"></exception>
    public virtual string StatusDisplayText => Status switch
    {
        JobStatus.Idle => "IDLE",
        JobStatus.Starting => "STARTING",
        JobStatus.Running => "RUNNING",
        JobStatus.Pausing => "PAUSING",
        JobStatus.Paused => "PAUSED",
        JobStatus.Stopping => "STOPPING",
        JobStatus.Resuming => "RESUMING",
        JobStatus.Waiting => throw new NotImplementedException(),
        _ => "UNKNOWN"
    };

    public virtual SolidColorBrush StatusColor => Status switch
    {
        JobStatus.Idle => new SolidColorBrush(Color.FromRgb(108, 117, 125)), // Gray
        JobStatus.Starting => new SolidColorBrush(Color.FromRgb(255, 193, 7)), // Yellow
        JobStatus.Running => new SolidColorBrush(Color.FromRgb(40, 167, 69)), // Green
        JobStatus.Pausing => new SolidColorBrush(Color.FromRgb(255, 193, 7)), // Yellow
        JobStatus.Paused => new SolidColorBrush(Color.FromRgb(253, 126, 20)), // Orange
        JobStatus.Stopping => new SolidColorBrush(Color.FromRgb(220, 53, 69)), // Red
        JobStatus.Resuming => new SolidColorBrush(Color.FromRgb(23, 162, 184)), // Blue
        JobStatus.Waiting => throw new NotImplementedException(),
        _ => new SolidColorBrush(Color.FromRgb(108, 117, 125))
    };

    public override void UpdateViewModel()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusDisplayText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(IdAndStatus));
    }

    public virtual int CPM => 0;
    public virtual float Progress => 0f;
    public virtual long TestedCount => 0;
    public virtual long TotalCount => 0;

    public string ElapsedString => $"{(int)Job.Elapsed.TotalDays} day(s) {Job.Elapsed:hh\\:mm\\:ss}";
    public string RemainingString =>
        Status == JobStatus.Idle || Status == JobStatus.Stopping || Progress >= 1.0f || TestedCount >= TotalCount
            ? "0 day(s) 00:00:00"
            : $"{(int)Job.Remaining.TotalDays} day(s) {Job.Remaining:hh\\:mm\\:ss}";

    /// <summary>
    /// Human-readable elapsed time (e.g., "2h 15m" instead of "0 day(s) 02:15:34")
    /// </summary>
    public string ElapsedStringHuman => FormatTimeSpanHuman(Job.Elapsed);

    /// <summary>
    /// Human-readable remaining time (e.g., "5h 30m" instead of "0 day(s) 05:30:00")
    /// </summary>
    public string RemainingStringHuman =>
        Status == JobStatus.Idle || Status == JobStatus.Stopping || Progress >= 1.0f || TestedCount >= TotalCount
            ? "--"
            : FormatTimeSpanHuman(Job.Remaining);

    /// <summary>
    /// Formats a TimeSpan into a human-readable string like "2d 5h", "3h 15m", or "45s"
    /// </summary>
    protected static string FormatTimeSpanHuman(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}d {span.Hours}h";
        }
        else if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }
        else if (span.TotalMinutes >= 1)
        {
            return $"{(int)span.TotalMinutes}m {span.Seconds}s";
        }
        else
        {
            return $"{span.Seconds}s";
        }
    }

    public virtual string ProgressString => $"{TestedCount} / {TotalCount} ({(Progress < 0 ? 0 : Progress * 100):0.00}%)";

    public virtual void PeriodicUpdate()
    {
        OnPropertyChanged(nameof(ElapsedString));
        OnPropertyChanged(nameof(RemainingString));
        OnPropertyChanged(nameof(ElapsedStringHuman));
        OnPropertyChanged(nameof(RemainingStringHuman));
        OnPropertyChanged(nameof(CPM));
    }

    public virtual void UpdateStats()
    {
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressString));
    }

    /// <summary>
    /// Updates the status of the job.
    /// </summary>
    public void UpdateStatus()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IdAndStatus));
    }
}

public class MultiRunJobViewModel(MultiRunJob job) : JobViewModel(job)
{
    private MultiRunJob MultiRunJob => Job as MultiRunJob;

    public string ConfigName => MultiRunJob.Config?.Metadata?.Name ?? "Config Missing";
    public string ConfigDisplayName => ConfigName;
    public string JobTypeDisplay => "Multi-Run Job";
    public string DataPoolInfo => MultiRunJob.DataPool switch
    {
        WordlistDataPool w => $"{w.Wordlist.Name} (Wordlist)",
        CombinationsDataPool => "Combinations",
        InfiniteDataPool => "Infinite",
        RangeDataPool => "Range",
        FileDataPool f => $"{Path.GetFileName(f.FileName)} (File)",
        _ => throw new NotImplementedException()
    };

    public string DataPoolDisplayInfo => MultiRunJob.DataPool switch
    {
        WordlistDataPool w => w.Wordlist.Name,
        CombinationsDataPool => "Combinations",
        InfiniteDataPool => "Infinite",
        RangeDataPool => "Range",
        FileDataPool f => Path.GetFileName(f.FileName),
        _ => "Unknown"
    };

    public int Bots => MultiRunJob.Bots;
    public int Skip => MultiRunJob.Skip;
    public JobProxyMode ProxyMode => MultiRunJob.ProxyMode;

    /// <summary>
    /// Stats
    /// </summary>
    public int DataTested => MultiRunJob.DataTested;
    public int DataHits => MultiRunJob.DataHits;
    public int DataCustom => MultiRunJob.DataCustom;
    public int DataToCheck => MultiRunJob.DataToCheck;
    public int DataFails => MultiRunJob.DataFails;
    public int DataRetried => MultiRunJob.DataRetried;
    public int DataBanned => MultiRunJob.DataBanned;
    public int DataErrors => MultiRunJob.DataErrors;
    public int DataInvalid => MultiRunJob.DataInvalid;

    /// <summary>
    /// Proxy stats
    /// </summary>
    public int ProxiesTotal => MultiRunJob.ProxiesTotal;
    public int ProxiesAlive => MultiRunJob.ProxiesAlive;
    public int ProxiesBad => MultiRunJob.ProxiesBad;
    public int ProxiesBanned => MultiRunJob.ProxiesBanned;

    public override int CPM => MultiRunJob.CPM;
    public override float Progress => MultiRunJob.Progress;
    public override long TestedCount => MultiRunJob.Status == JobStatus.Idle ? Skip : DataTested + Skip;
    public override long TotalCount => MultiRunJob.DataPool.Size;

    public decimal CaptchaCredit => MultiRunJob.CaptchaCredit;

    /// <summary>
    /// Update properties that only need to be updated every second.
    /// </summary>
    public override void PeriodicUpdate()
    {
        base.PeriodicUpdate();
        OnPropertyChanged(nameof(CaptchaCredit));

        OnPropertyChanged(nameof(DataRetried));
        OnPropertyChanged(nameof(DataBanned));
        OnPropertyChanged(nameof(DataErrors));
        OnPropertyChanged(nameof(DataInvalid));

        OnPropertyChanged(nameof(ProxiesTotal));
        OnPropertyChanged(nameof(ProxiesAlive));
        OnPropertyChanged(nameof(ProxiesBad));
        OnPropertyChanged(nameof(ProxiesBanned));
    }

    /// <summary>
    /// Update properties that need to be updated every time there is a result.
    /// </summary>
    public override void UpdateStats()
    {
        base.UpdateStats();
        OnPropertyChanged(nameof(DataTested));
        OnPropertyChanged(nameof(DataHits));
        OnPropertyChanged(nameof(DataCustom));
        OnPropertyChanged(nameof(DataToCheck));
        OnPropertyChanged(nameof(DataFails));
    }

    /// <summary>
    /// Update the Bots property.
    /// </summary>
    public void UpdateBots() => OnPropertyChanged(nameof(Bots));

    /// <summary>
    /// Update the Skip property.
    /// </summary>
    public void UpdateSkip() => OnPropertyChanged(nameof(Skip));



    public override void UpdateViewModel()
    {
        base.UpdateViewModel();
        OnPropertyChanged(nameof(ConfigName));
        OnPropertyChanged(nameof(ConfigDisplayName));
        OnPropertyChanged(nameof(DataPoolInfo));
        OnPropertyChanged(nameof(DataPoolDisplayInfo));
    }
}

public class ProxyCheckJobViewModel(ProxyCheckJob job) : JobViewModel(job)
{
    private ProxyCheckJob ProxyCheckJob => Job as ProxyCheckJob;

    public string ConfigDisplayName => "Proxy Check";
    public string JobTypeDisplay => "Proxy Check Job";
    public string DataPoolDisplayInfo => $"URL: {Url}";

    public int Bots => ProxyCheckJob.Bots;
    public string Url => ProxyCheckJob.Url;
    public string SuccessKey => ProxyCheckJob.SuccessKey;
    public bool CheckOnlyUntested => ProxyCheckJob.CheckOnlyUntested;
    public int TimeoutMilliseconds => (int)ProxyCheckJob.Timeout.TotalMilliseconds;

    public int Total => ProxyCheckJob.Total;
    public int Tested => ProxyCheckJob.Tested;
    public int Working => ProxyCheckJob.Working;
    public int NotWorking => ProxyCheckJob.NotWorking;

    /// <summary>
    /// For consistency with MultiRun jobs
    /// </summary>
    public int DataHits => Working;
    /// <summary>
    /// Proxy check doesn&#39;t have custom results
    public override int CPM => ProxyCheckJob.CPM;
    public override float Progress => ProxyCheckJob.Progress;
    public override long TestedCount => Tested;
    public override long TotalCount => Total;

    /// <summary>
    /// Update properties that only need to be updated every second.
    /// </summary>
    public override void PeriodicUpdate()
    {
        base.PeriodicUpdate();
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Tested));
        OnPropertyChanged(nameof(Working));
        OnPropertyChanged(nameof(NotWorking));
    }

    /// <summary>
    /// Update properties that need to be updated every time there is a result.
    /// </summary>
    public override void UpdateStats()
    {
        base.UpdateStats();
        OnPropertyChanged(nameof(DataHits));
    }

    /// <summary>
    /// Update the Bots property.
    /// </summary>
    public void UpdateBots() => OnPropertyChanged(nameof(Bots));



    public override void UpdateViewModel()
    {
        base.UpdateViewModel();
        OnPropertyChanged(nameof(ConfigDisplayName));
        OnPropertyChanged(nameof(DataPoolDisplayInfo));
        OnPropertyChanged(nameof(DataHits));
    }
}


