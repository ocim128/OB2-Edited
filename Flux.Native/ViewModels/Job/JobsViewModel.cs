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
    private readonly IJobOrchestrator jobOrchestrator;
    private readonly HotkeyService hotkeyService;
    private readonly Timer timer;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private volatile bool disposed;

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
        IJobOrchestrator jobOrchestrator,
        HotkeyService hotkeyService)
    {
        this.jobOrchestrator = jobOrchestrator ?? throw new ArgumentNullException(nameof(jobOrchestrator));
        this.hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));

        // Start the timer which will trigger the first refresh after 200ms,
        // then every 2 seconds. Avoids blocking the UI thread in the constructor.
        timer = new Timer(_ => _ = RefreshJobsAsync(), null, 200, 2000);
    }

    public async Task<JobViewModel> CreateJobAsync(JobOptions options)
    {
        var jobId = await jobOrchestrator.CreateJobAsync(options).ConfigureAwait(false);
        await RefreshJobsAsync().ConfigureAwait(false);
        return JobsCollection.FirstOrDefault(job => job.Id == jobId) ?? throw new InvalidOperationException($"Job {jobId} not found after refresh");
    }

    public Task<JobOptionsSnapshotDto?> GetJobOptionsAsync(int jobId, bool clone = false)
        => jobOrchestrator.GetJobOptionsAsync(jobId, clone);

    public async Task<JobViewModel> EditJobAsync(int jobId, JobOptions options)
    {
        var updatedJobId = await jobOrchestrator.UpdateJobAsync(jobId, options).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Job {jobId} could not be updated");

        await RefreshJobsAsync().ConfigureAwait(false);
        return JobsCollection.FirstOrDefault(job => job.Id == updatedJobId) ?? throw new InvalidOperationException($"Job {updatedJobId} not found after refresh");
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
        if (disposed)
        {
            return;
        }

        if (!await refreshLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (disposed)
            {
                return;
            }

            var latestJobs = await jobOrchestrator.GetDesktopJobsAsync().ConfigureAwait(false);
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
        if (disposed)
        {
            return;
        }

        var filteredJobs = allJobs
            .Where(JobMatchesSearch)
            .OrderBy(static job => job.Id)
            .ToList();

        RunOnUiThread(() => SyncJobCollection(JobsCollection, filteredJobs));
    }

    private static void SyncJobCollection(ObservableCollection<JobViewModel> collection, List<DesktopJobListItemDto> target)
    {
        var existingByKey = collection.ToDictionary(static job => (job.Id, job.JobType));
        var targetViewModels = new List<JobViewModel>(target.Count);

        foreach (var snapshot in target)
        {
            var key = (snapshot.Id, snapshot.JobType);
            if (existingByKey.TryGetValue(key, out var jobViewModel))
            {
                jobViewModel.ApplySnapshot(snapshot);
            }
            else
            {
                jobViewModel = MakeViewModel(snapshot);
            }

            targetViewModels.Add(jobViewModel);
        }

        SyncCollection(collection, targetViewModels);
    }

    /// <summary>
    /// Syncs an ObservableCollection to match a target list using diff-based updates.
    /// Only adds/removes items that changed, avoiding N+1 CollectionChanged events.
    /// </summary>
    private static void SyncCollection<T>(ObservableCollection<T> collection, List<T> target) where T : notnull
    {
        var i = 0;
        while (i < Math.Min(collection.Count, target.Count))
        {
            if (!EqualityComparer<T>.Default.Equals(collection[i], target[i]))
            {
                // Check if target[i] is further ahead (items were removed)
                var existingIndex = collection.IndexOf(target[i]);
                if (existingIndex >= 0)
                {
                    // Remove items before it
                    while (i < existingIndex)
                    {
                        collection.RemoveAt(i);
                        existingIndex--;
                    }
                }
                else
                {
                    collection.Insert(i, target[i]);
                    i++;
                }
            }
            else
            {
                i++;
            }
        }

        // Remove trailing extras
        while (collection.Count > target.Count)
        {
            collection.RemoveAt(collection.Count - 1);
        }

        // Add remaining new items
        while (collection.Count < target.Count)
        {
            collection.Add(target[collection.Count]);
        }
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

    private void RunOnUiThread(Action action)
    {
        if (disposed)
        {
            return;
        }

        if (Application.Current?.Dispatcher is null || Application.Current.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!disposed)
            {
                action();
            }
        });
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer?.Dispose();
    }
}
