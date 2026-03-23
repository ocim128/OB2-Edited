using RuriLib.Helpers;
using RuriLib.Models.Jobs.Status;
using RuriLib.Parallelization;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Models.Jobs;

internal sealed class JobLifecycleService
{
    private readonly WorkItemFactory _workItemFactory = new();

    public async Task StartAsync(MultiRunJob job, RuriLib.Services.RuriLibSettingsService settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job.ExecutionCoordinator);

        var workFunction = new Func<MultiRunInput, CancellationToken, Task<CheckResult>>(
            (input, token) => job.ExecutionCoordinator.ExecuteAsync(input, token));

        job.UpdateStatus(JobStatus.Waiting);
        await job.WaitForStartConditionAsync(cancellationToken).ConfigureAwait(false);
        job.UpdateStatus(JobStatus.Starting);

        var wordlistType = settings.Environment.WordlistTypes.FirstOrDefault(t => t.Name == job.DataPool.WordlistType);
        var workItems = _workItemFactory.Create(job, wordlistType);

        var parallelizer = ParallelizerFactory<MultiRunInput, CheckResult>
            .Create(
                settings.RuriLibSettings.GeneralSettings.ParallelizerType,
                workItems,
                workFunction,
                job.Bots,
                job.DataPool.Size,
                job.Skip,
                job.BotLimit);

        parallelizer.CPMLimit = job.Config.Settings.GeneralSettings.MaximumCPM;
        parallelizer.NewResult += job.ResultProcessor.HandleDataProcessed;
        parallelizer.StatusChanged += job.HandleParallelizerStatusChanged;
        parallelizer.TaskError += job.HandleTaskError;
        parallelizer.Error += job.HandleParallelizerError;
        parallelizer.NewResult += job.ResultProcessor.HandleParallelizerResult;
        parallelizer.Completed += job.HandleParallelizerCompleted;

        job.Parallelizer = parallelizer;
        job.ResetRuntimeStats();
        StartTimers(job);
        job.LogInfo("All set, starting the execution");
        await parallelizer.Start().ConfigureAwait(false);
    }

    public async Task StopAsync(MultiRunJob job)
    {
        try
        {
            if (job.Parallelizer is not null)
            {
                await job.Parallelizer.Stop().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            job.RaiseError(ex);
            throw;
        }
        finally
        {
            StopTimers(job);
            job.LogInfo("Execution stopped");
            job.DisposeRuntimeResources();
        }
    }

    public async Task AbortAsync(MultiRunJob job)
    {
        try
        {
            if (job.Parallelizer is not null)
            {
                await job.Parallelizer.Abort().ConfigureAwait(false);
            }

            if (job.StartCancellationSource is not null)
            {
                await job.StartCancellationSource.CancelAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            job.RaiseError(ex);
            throw;
        }
        finally
        {
            StopTimers(job);
            job.LogInfo("Execution aborted");
            job.DisposeRuntimeResources();
        }
    }

    public async Task PauseAsync(MultiRunJob job)
    {
        try
        {
            if (job.Parallelizer is not null)
            {
                await job.Parallelizer.Pause().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            job.RaiseError(ex);
            throw;
        }
        finally
        {
            StopTimers(job);
            job.LogInfo("Execution paused");
        }
    }

    public async Task ResumeAsync(MultiRunJob job)
    {
        try
        {
            if (job.Parallelizer is not null)
            {
                await job.Parallelizer.Resume().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            job.RaiseError(ex);
            throw;
        }

        StartTimers(job);
        job.LogInfo("Execution resumed");
    }

    public async Task ReloadProxiesAsync(MultiRunJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job.ProxyPool);
        ArgumentNullException.ThrowIfNull(job.RuntimeAsyncLocker);

        IDisposable? releaser = null;
        try
        {
            releaser = await job.RuntimeAsyncLocker
                .Acquire(typeof(RuriLib.Models.Proxies.ProxyPool), nameof(RuriLib.Models.Proxies.ProxyPool.ReloadAllAsync), cancellationToken)
                .ConfigureAwait(false);

            await job.ProxyPool.ReloadAllAsync(job.ShuffleProxies, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            releaser?.Dispose();
        }
    }

    private void StartTimers(MultiRunJob job)
    {
        StopTimers(job);

        job.TickTimer = new Timer(
            _ => job.RaiseTimerTick(),
            null,
            (int)job.TickInterval.TotalMilliseconds,
            (int)job.TickInterval.TotalMilliseconds);

        if (job.PeriodicReloadInterval <= TimeSpan.Zero || job.ProxyPool is null || job.RuntimeAsyncLocker is null)
        {
            return;
        }

        job.ProxyReloadTimer = new Timer(
            _ => _ = ReloadProxyPoolSafelyAsync(job),
            null,
            (int)job.PeriodicReloadInterval.TotalMilliseconds,
            (int)job.PeriodicReloadInterval.TotalMilliseconds);
    }

    private static void StopTimers(MultiRunJob job)
    {
        job.TickTimer?.Dispose();
        job.ProxyReloadTimer?.Dispose();
        job.TickTimer = null;
        job.ProxyReloadTimer = null;
    }

    private async Task ReloadProxyPoolSafelyAsync(MultiRunJob job)
    {
        try
        {
            await ReloadProxiesAsync(job).ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }
    }
}
