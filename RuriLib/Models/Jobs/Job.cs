using RuriLib.Logging;
using RuriLib.Models.Jobs.StartConditions;
using RuriLib.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Models.Jobs;

// Todo: Implement IDisposable and dispose the following when a job is deleted or edited
// - GroupProxySource
// - DatabaseHitOutput
// - DatabaseProxyCheckOutput
public abstract class Job(RuriLibSettingsService settings, PluginRepository pluginRepo, IJobLogger logger = null) : IDisposable
{
    /// <summary>
    /// Public properties
    /// </summary>
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public JobStatus Status { get; protected set; } = JobStatus.Idle;
    public DateTime CreationTime { get; set; } = DateTime.Now;
    public DateTime StartTime { get; set; } = DateTime.Now;
    public StartCondition StartCondition { get; set; } = new RelativeTimeStartCondition();
    public virtual TimeSpan Elapsed => DateTime.Now - StartTime;
    public virtual TimeSpan Remaining => throw new NotImplementedException();

    /// <summary>
    /// Virtual properties
    /// </summary>
    /// <exception cref="NotImplementedException"></exception>
    public virtual float Progress => throw new NotImplementedException();

    /// <summary>
    /// Protected fields
    /// </summary>
    protected readonly RuriLibSettingsService settings = settings;
    protected readonly PluginRepository pluginRepo = pluginRepo;
    protected readonly IJobLogger logger = logger;

    /// <summary>
    /// Private fields
    /// </summary>
    private bool _waitFinished;
    /// <summary>
    /// Cancellation token for cancelling the StartCondition wait
    /// </summary>
    private CancellationTokenSource _cts;

    public virtual async Task Start(CancellationToken cancellationToken = default)
    {
        _waitFinished = false;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        StartTime = DateTime.Now;

        try
        {
            logger?.LogInfo(Id, "Waiting for the start condition to be verified...");
            await StartCondition.WaitUntilVerified(this, _cts.Token);
            logger?.LogInfo(Id, "Finished waiting");
        }
        catch (TaskCanceledException)
        {
            // The token has been cancelled, skip the wait
            logger?.LogInfo(Id, "The wait has been manually skipped");
        }

        _waitFinished = true;
    }

    public void SkipWait()
    {
        if (!_waitFinished && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }

    public virtual Task Pause() => throw new NotImplementedException();

    public virtual Task Resume() => throw new NotImplementedException();

    public virtual Task Stop() => throw new NotImplementedException();

    public virtual Task Abort() => throw new NotImplementedException();

    public void Dispose() => throw new NotImplementedException();
}
