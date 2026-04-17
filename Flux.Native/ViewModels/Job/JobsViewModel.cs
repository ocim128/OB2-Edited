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

public partial class JobsViewModel : ViewModelBase, IDisposable
{
    private readonly IJobQueries jobQueries;
    private readonly IJobOrchestrator jobOrchestrator;
    private readonly HotkeyService hotkeyService;
    private readonly Timer timer;
    private readonly SemaphoreSlim refreshLock = new(1, 1);

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

        RunOnUiThread(() =>
        {
            JobsCollection.Clear();
            foreach (var job in filteredJobs)
                JobsCollection.Add(job);
        });
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

        Application.Current.Dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        timer?.Dispose();
        refreshLock?.Dispose();
    }
}
