using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenBullet2.Core.Entities;
using OpenBullet2.Core.Extensions;
using OpenBullet2.Core.Models.Data;
using OpenBullet2.Core.Models.Jobs;
using OpenBullet2.Core.Repositories;
using RuriLib.Models.Data.DataPools;
using RuriLib.Models.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenBullet2.Core.Services;

/// <summary>
/// Manages multiple jobs.
/// </summary>
public class JobManagerService : IDisposable
{
    /// <summary>
    /// The list of all created jobs.
    /// </summary>
    public IEnumerable<Job> Jobs => _jobs;
    private readonly List<Job> _jobs = new();
    private readonly Dictionary<int, (DateTime LastSave, int LastDataTested)> _jobSaveStates = new();

    private readonly SemaphoreSlim _jobSemaphore = new(1, 1);
    private readonly SemaphoreSlim _recordSemaphore = new(1, 1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobManagerService> _logger;

    public JobManagerService(IServiceScopeFactory scopeFactory, JobFactoryService jobFactory, ILogger<JobManagerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        InitializeJobsAsync(scopeFactory, jobFactory).Forget(
            ex => _logger.LogError(ex, "Failed to initialize jobs"));
    }

    private async Task InitializeJobsAsync(IServiceScopeFactory scopeFactory, JobFactoryService jobFactory)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

            // Restore jobs from the database
            var entities = jobRepo.GetAll().Include(j => j.Owner).ToList();
            var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };

            foreach (var entity in entities)
            {
                // Convert old namespaces to support old databases
                if (entity.JobOptions.Contains("OpenBullet2.Models") || entity.JobOptions.Contains(", OpenBullet2\""))
                {
                    entity.JobOptions = entity.JobOptions
                        .Replace("OpenBullet2.Models", "OpenBullet2.Core.Models")
                        .Replace(", OpenBullet2\"", ", OpenBullet2.Core\"");

                    await jobRepo.UpdateAsync(entity).ConfigureAwait(false);
                }

                var options = JsonConvert.DeserializeObject<JobOptionsWrapper>(entity.JobOptions, jsonSettings).Options;
                var job = await jobFactory.FromOptionsAsync(entity.Id, entity.Owner == null ? 0 : entity.Owner.Id, options).ConfigureAwait(false);
                AddJob(job);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize jobs");
        }
    }

    public void AddJob(Job job)
    {
        _jobs.Add(job);

        if (job is MultiRunJob mrj)
        {
            mrj.OnCompleted += MultiRunJobOnCompleted;
            mrj.OnTimerTick += MultiRunJobOnTimerTick;
            mrj.OnBotsChanged += MultiRunJobOnBotsChanged;
        }
    }

    public void RemoveJob(Job job)
    {
        _jobs.Remove(job);
        _jobSaveStates.Remove(job.Id);

        if (job is MultiRunJob mrj)
        {
            try
            {
                mrj.OnCompleted -= MultiRunJobOnCompleted;
                mrj.OnTimerTick -= MultiRunJobOnTimerTick;
                mrj.OnBotsChanged -= MultiRunJobOnBotsChanged;
            }
            catch
            {

            }
        }
    }

    public void Clear()
    {
        UnbindAllEvents();
        _jobs.Clear();
        _jobSaveStates.Clear();
    }

    private void MultiRunJobOnCompleted(object? sender, EventArgs e)
    {
        if (sender is MultiRunJob job)
        {
            SaveRecordAsync(job, includeRuntimeProgress: false).Forget(
                ex => _logger.LogError(ex, "Failed to save record for job {JobId}", job.Id));
            SaveMultiRunJobOptionsAsync(job, includeRuntimeProgress: false).Forget(
                ex => _logger.LogError(ex, "Failed to save options for job {JobId}", job.Id));
        }
    }

    private void MultiRunJobOnTimerTick(object? sender, EventArgs e)
    {
        if (sender is not MultiRunJob job)
        {
            return;
        }

        // Throttling logic to prevent excessive DB writes
        var now = DateTime.UtcNow;
        if (_jobSaveStates.TryGetValue(job.Id, out var state))
        {
            var timeSinceLastSave = now - state.LastSave;
            var dataChanged = job.DataTested - state.LastDataTested;

            // Save only if > 10 seconds elapsed OR > 50 data points processed OR data counter reset (unexpected)
            if (timeSinceLastSave.TotalSeconds < 10 && dataChanged < 50 && dataChanged >= 0)
            {
                return;
            }
        }
        
        _jobSaveStates[job.Id] = (now, job.DataTested);

        var includeRuntimeProgress = IsActivelyProcessing(job.Status);
        
        SaveRecordAsync(job, includeRuntimeProgress).Forget(
            ex => _logger.LogError(ex, "Failed to periodically save record for job {JobId}", job.Id));
        SaveMultiRunJobOptionsAsync(job, includeRuntimeProgress).Forget(
            ex => _logger.LogError(ex, "Failed to periodically save options for job {JobId}", job.Id));
    }

    private void MultiRunJobOnBotsChanged(object? sender, EventArgs e)
    {
        if (sender is MultiRunJob job)
        {
            var includeRuntimeProgress = IsActivelyProcessing(job.Status);
            SaveMultiRunJobOptionsAsync(job, includeRuntimeProgress).Forget(
                ex => _logger.LogError(ex, "Failed to save options after bots change for job {JobId}", job.Id));
        }
    }

    private static bool IsActivelyProcessing(JobStatus status) => status is JobStatus.Starting
        or JobStatus.Running
        or JobStatus.Pausing
        or JobStatus.Paused
        or JobStatus.Resuming
        or JobStatus.Stopping;

    // Saves the record for a MultiRunJob in the IRecordRepository. Thread safe.
    private async Task SaveRecordAsync(MultiRunJob job, bool includeRuntimeProgress)
    {
        if (job.DataPool is not WordlistDataPool pool)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var recordRepo = scope.ServiceProvider.GetRequiredService<IRecordRepository>();

        await _recordSemaphore.WaitAsync();

        try
        {
            var record = await recordRepo.GetAll()
                    .FirstOrDefaultAsync(r => r.ConfigId == job.Config.Id && r.WordlistId == pool.Wordlist.Id);

            var checkpoint = includeRuntimeProgress
                ? job.Skip + job.DataTested
                : job.Skip;

            if (record == null)
            {
                await recordRepo.AddAsync(new RecordEntity
                {
                    ConfigId = job.Config.Id,
                    WordlistId = pool.Wordlist.Id,
                    Checkpoint = checkpoint
                });
            }
            else
            {
                record.Checkpoint = checkpoint;
                await recordRepo.UpdateAsync(record);
            }
        }
        catch
        {

        }
        finally
        {
            _recordSemaphore.Release();
        }
    }

    // Saves the options for a MultiRunJob in the IJobRepository. Thread safe.
    public async Task SaveMultiRunJobOptionsAsync(MultiRunJob job, bool includeRuntimeProgress = true)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        await _jobSemaphore.WaitAsync();

        try
        {
            var entity = await jobRepo.GetAsync(job.Id);

            if (entity == null || entity.JobOptions == null)
            {
                Console.WriteLine("Skipped job options save because Job (or JobOptions) was null");
                return;
            }

            // Deserialize and unwrap the job options
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var wrapper = JsonConvert.DeserializeObject<JobOptionsWrapper>(entity.JobOptions, settings);
            var options = (MultiRunJobOptions)wrapper.Options;

            // Check if it's valid
            if (string.IsNullOrEmpty(options.ConfigId))
            {
                Console.WriteLine("Skipped job options save because ConfigId was null");
                return;
            }

            if (options.DataPool is WordlistDataPoolOptions x && x.WordlistId == -1)
            {
                Console.WriteLine("Skipped job options save because WordlistId was -1");
                return;
            }

            // Update the skip (optionally include in-flight progress) and the bots
            var checkpoint = includeRuntimeProgress
                ? job.Skip + job.DataTested
                : job.Skip;

            options.Skip = checkpoint;

            options.Bots = job.Bots;

            // Wrap and serialize again
            var newWrapper = new JobOptionsWrapper { Options = options };
            entity.JobOptions = JsonConvert.SerializeObject(newWrapper, settings);

            // Update the job
            await jobRepo.UpdateAsync(entity);
        }
        catch
        {

        }
        finally
        {
            _jobSemaphore.Release();
        }
    }

    private void UnbindAllEvents()
    {
        foreach (var job in _jobs)
        {
            if (job is MultiRunJob mrj)
            {
                try
                {
                    mrj.OnCompleted -= MultiRunJobOnCompleted;
                    mrj.OnTimerTick -= MultiRunJobOnTimerTick;
                    mrj.OnBotsChanged -= MultiRunJobOnBotsChanged;
                }
                catch
                {

                }
            }
        }
    }

    public void Dispose() => UnbindAllEvents();
}
