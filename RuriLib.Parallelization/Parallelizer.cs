using RuriLib.Parallelization.Exceptions;
using RuriLib.Parallelization.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Parallelization;

/// <summary>
/// Provides a managed way to execute parallelized work.
/// </summary>
/// <typeparam name="TInput">The type of the workload items</typeparam>
/// <typeparam name="TOutput">The type of the results</typeparam>
public abstract class Parallelizer<TInput, TOutput> : IDisposable
{
    #region Public Fields
    /// <summary>
    /// The maximum value that the degree of parallelism can have when changed through the
    /// <see cref="Parallelizer{TInput, TOutput}.ChangeDegreeOfParallelism(int)"/> method.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 200;

    /// <summary>
    /// The current status of the parallelizer.
    /// </summary>
    public ParallelizerStatus Status
    {
        get => status;
        protected set
        {
            status = value;
            OnStatusChanged(status);
        }
    }

    /// <summary>
    /// Retrieves the current progress in the interval [0, 1].
    /// The progress is -1 if the manager hasn't been started yet.
    /// </summary>
    public float Progress
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var tot = totalAmount;
            if (tot <= 0) return -1f;
            // Use volatile read to avoid torn reads across threads
            var proc = Volatile.Read(ref processed);
            var value = (float)(proc + skip) / tot;
            return value > 1f ? 1f : value;
        }
    }

    /// <summary>
    /// Retrieves the completed work per minute.
    /// </summary>
    public int CPM { get; protected set; }

    /// <summary>
    /// Sets a maximum threshold for CPM. 0 to disable.
    /// </summary>
    public int CPMLimit { get; set; }

    /// <summary>
    /// The time when the parallelizer started its work for its last running session.
    /// </summary>
    public DateTime StartTime { get; private set; }

    /// <summary>
    /// The time when the parallelizer finished its work or was stopped (<see langword="null"/> if it hasn't finished
    /// a single session yet).
    /// </summary>
    public DateTime? EndTime { get; private set; }

    /// <summary>
    /// The Estimated Time of Arrival (when the parallelizer is expected to finish all the work).
    /// </summary>
    public DateTime ETA
    {
        get
        {
            var cpm = CPM;
            var prog = Progress;
            if (cpm <= 0 || prog < 0f) return DateTime.MaxValue;
            var remaining = (double)totalAmount * (1d - prog);
            var minutes = remaining / cpm;
            return minutes < TimeSpan.MaxValue.TotalMinutes
                ? StartTime + TimeSpan.FromMinutes(minutes)
                : DateTime.MaxValue;
        }
    }

    /// <summary>
    /// The time elapsed since the start of the session.
    /// </summary>
    public TimeSpan Elapsed => stopwatch.Elapsed;

    /// <summary>
    /// The expected remaining time to finish all the work.
    /// </summary>
    public TimeSpan Remaining => EndTime.HasValue ? TimeSpan.Zero : ETA - DateTime.Now;
    #endregion

    #region Protected Fields
    /// <summary>
    /// The status of the parallelizer.
    /// </summary>
    protected ParallelizerStatus status = ParallelizerStatus.Idle;

    /// <summary>
    /// The number of items that can be processed concurrently.
    /// </summary>
    protected int degreeOfParallelism;

    /// <summary>
    /// The items to process.
    /// </summary>
    protected readonly IEnumerable<TInput> workItems;

    /// <summary>
    /// The function to process items and get results.
    /// </summary>
    protected readonly Func<TInput, CancellationToken, Task<TOutput>> workFunction;

    /// <summary>
    /// The function that turns each input item into an awaitable <see cref="Task"/>.
    /// </summary>
    protected readonly Func<TInput, Task> taskFunction;

    /// <summary>
    /// The total amount of work items that are expected to be enumerated (for progress calculations).
    /// </summary>
    protected readonly long totalAmount;

    /// <summary>
    /// The number of items to skip from the start of the collection (to restore previously aborted sessions).
    /// </summary>
    protected readonly int skip;

    /// <summary>
    /// The current amount of work items that were processed so far.
    /// </summary>
    protected int processed;

    /// <summary>
    /// The queue of timestamps for CPM calculation. Using a ConcurrentQueue for thread safety
    /// and efficient enqueue/dequeue operations.
    /// </summary>
    protected readonly ConcurrentQueue<long> checkedTimestamps = new();

    /// <summary>
    /// A timer that periodically updates the CPM and progress.
    /// </summary>
    private Timer _updateTimer;

    /// <summary>
    /// A lock that can be used to update the CPM from a single thread at a time.
    /// </summary>
    protected readonly object cpmLock = new();

    /// <summary>
    /// The stopwatch that calculates the elapsed time.
    /// </summary>
    protected readonly Stopwatch stopwatch = new();

    /// <summary>
    /// A token that can be used to pause the execution of the parallelizer.
    /// </summary>
    protected readonly PauseTokenSource pauseTokenSource = new();

    /// <summary>
    /// The pause token.
    /// </summary>
    protected PauseToken pauseToken;

    /// <summary>
    /// A soft cancellation token. Cancel this for soft AND hard abort.
    /// </summary>
    protected CancellationTokenSource softCTS;

    /// <summary>
    /// A hard cancellation token. Cancel this for hard abort only.
    /// </summary>
    protected CancellationTokenSource hardCTS;
    #endregion

    #region Events
    /// <summary>Called when an operation throws an exception.</summary>
    public event EventHandler<ErrorDetails<TInput>> TaskError;

    /// <summary>
    /// Invokes a <see cref="Parallelizer{TInput, TOutput}.TaskError"/> event.
    /// </summary>
    /// <param name="input"></param>
    protected virtual void OnTaskError(ErrorDetails<TInput> input) => TaskError?.Invoke(this, input);

    /// <summary>Called when the <see cref="Parallelizer{TInput, TOutput}"/> itself throws an exception.</summary>
    public event EventHandler<Exception> Error;

    /// <summary>
    /// Invokes a <see cref="Parallelizer{TInput, TOutput}.Error"/> event.
    /// </summary>
    /// <param name="ex"></param>
    protected virtual void OnError(Exception ex) => Error?.Invoke(this, ex);

    /// <summary>Called when an operation is completed successfully.</summary>
    public event EventHandler<ResultDetails<TInput, TOutput>> NewResult;

    /// <summary>
    /// Invokes a <see cref="Parallelizer{TInput, TOutput}.NewResult"/> event.
    /// </summary>
    /// <param name="result"></param>
    protected virtual void OnNewResult(ResultDetails<TInput, TOutput> result) => NewResult?.Invoke(this, result);

    /// <summary>Called when the progress changes.</summary>
    public event EventHandler<float> ProgressChanged;

    /// <summary>
    /// Invokes a <see cref="Parallelizer{TInput, TOutput}.ProgressChanged"/> event.
    /// </summary>
    /// <param name="progress"></param>
    protected virtual void OnProgressChanged(float progress) => ProgressChanged?.Invoke(this, progress);

    /// <summary>Called when all operations were completed successfully.</summary>
    public event EventHandler Completed;

    /// <summary>
    /// Invokes a <see cref="Parallelizer{TInput, TOutput}.Completed"/> event.
    /// </summary>
    protected virtual void OnCompleted() => Completed?.Invoke(this, EventArgs.Empty);

    /// <summary>Called when <see cref="Status"/> changes.</summary>
    public event EventHandler<ParallelizerStatus> StatusChanged;

    /// <summary>
    /// Invokes a <see cref="Parallelizer{TInput, TOutput}.StatusChanged"/> event.
    /// </summary>
    /// <param name="newStatus"></param>
    protected virtual void OnStatusChanged(ParallelizerStatus newStatus) => StatusChanged?.Invoke(this, newStatus);
    #endregion

    #region Constructors
    /// <summary>
    /// Creates a new instance of <see cref="Parallelizer{TInput, TOutput}"/>.
    /// </summary>
    /// <param name="workItems">The collection of data to process in parallel</param>
    /// <param name="workFunction">The work function that must be executed on the data</param>
    /// <param name="degreeOfParallelism">The amount of concurrent tasks that can be started</param>
    /// <param name="totalAmount">The total amount of data that is expected from <paramref name="workItems"/></param>
    /// <param name="skip">The amount of <paramref name="workItems"/> to skip at the beginning</param>
    /// <param name="maxDegreeOfParallelism">The maximum degree of parallelism that can be set</param>
    protected Parallelizer(IEnumerable<TInput> workItems, Func<TInput, CancellationToken, Task<TOutput>> workFunction,
        int degreeOfParallelism, long totalAmount, int skip = 0, int maxDegreeOfParallelism = 200)
    {
        if (degreeOfParallelism < 1)
        {
            throw new ArgumentException("The degree of parallelism must be greater than 1");
        }

        if (degreeOfParallelism > maxDegreeOfParallelism)
        {
            throw new ArgumentException("The degree of parallelism must not be greater than the maximum degree of parallelism");
        }

        if (skip >= totalAmount)
        {
            throw new ArgumentException("The skip must be less than the total amount");
        }

        this.workItems = workItems ?? throw new ArgumentNullException(nameof(workItems));
        this.workFunction = workFunction ?? throw new ArgumentNullException(nameof(workFunction));
        this.totalAmount = totalAmount;
        this.degreeOfParallelism = degreeOfParallelism;
        this.skip = skip;
        MaxDegreeOfParallelism = maxDegreeOfParallelism;

        pauseToken = pauseTokenSource.Token;

        // Assign the task function
        taskFunction = new Func<TInput, Task>(async item =>
        {
            if (softCTS.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var workResult = await workFunction.Invoke(item, hardCTS.Token).ConfigureAwait(false);
                OnNewResult(new ResultDetails<TInput, TOutput>(item, workResult));
                hardCTS.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (hardCTS.IsCancellationRequested || softCTS.IsCancellationRequested)
            {
                // Swallow expected cancellations to avoid noisy TaskError spam
            }
            catch (Exception ex)
            {
                OnTaskError(new ErrorDetails<TInput>(item, ex));
            }
            finally
            {
                // Use TickCount64 to avoid 24.9-day wraparound issues
                checkedTimestamps.Enqueue(Environment.TickCount64);
                _ = Interlocked.Increment(ref processed);
            }
        });
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Starts the execution (without waiting for completion).
    /// </summary>
    /// <exception cref="RequiredStatusException"></exception>
    public virtual Task Start()
    {
        if (Status != ParallelizerStatus.Idle)
        {
            throw new RequiredStatusException(ParallelizerStatus.Idle, Status);
        }

        StartTime = DateTime.Now;
        EndTime = null;

        // Clear the queue by dequeuing any existing items
        while (checkedTimestamps.TryDequeue(out _)) { }

        softCTS = new CancellationTokenSource();
        hardCTS = new CancellationTokenSource();

        stopwatch.Restart();
        _updateTimer = new Timer(_ =>
        {
            UpdateCPM(Environment.TickCount64);
            OnProgressChanged(Progress);
        }, null, 1000, 1000);

        return Task.CompletedTask;
    }

    /// <summary>Pauses the execution (waits until the ongoing operations are completed).</summary>
    /// <exception cref="RequiredStatusException"></exception>
    public virtual Task Pause()
    {
        if (Status != ParallelizerStatus.Running)
        {
            throw new RequiredStatusException(ParallelizerStatus.Running, Status);
        }

        _updateTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        stopwatch.Stop();

        return pauseTokenSource.PauseAsync();
    }

    /// <summary>Resumes a paused execution.</summary>
    /// <exception cref="RequiredStatusException"></exception>
    public virtual Task Resume()
    {
        if (Status != ParallelizerStatus.Paused)
        {
            throw new RequiredStatusException(ParallelizerStatus.Paused, Status);
        }

        _updateTimer?.Change(1000, 1000);
        stopwatch.Start();

        return pauseTokenSource.ResumeAsync();
    }

    /// <summary>
    /// Stops the execution (waits for the current items to finish).
    /// </summary>
    /// <exception cref="RequiredStatusException"></exception>
    public virtual Task Stop()
    {
        if (Status is not ParallelizerStatus.Running and not ParallelizerStatus.Paused)
        {
            throw new RequiredStatusException([ParallelizerStatus.Running, ParallelizerStatus.Paused], Status);
        }

        _updateTimer?.Dispose();
        _updateTimer = null;
        stopwatch.Stop();
        EndTime = DateTime.Now;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Aborts the execution without waiting for the current work to finish.
    /// </summary>
    /// <exception cref="RequiredStatusException"></exception>
    public virtual Task Abort()
    {
        if (Status is not ParallelizerStatus.Running and not ParallelizerStatus.Paused and not ParallelizerStatus.Stopping
            and not ParallelizerStatus.Pausing)
        {
            throw new RequiredStatusException([ParallelizerStatus.Running, ParallelizerStatus.Paused, ParallelizerStatus.Stopping, ParallelizerStatus.Pausing],
            Status);
        }

        _updateTimer?.Dispose();
        _updateTimer = null;
        stopwatch.Stop();
        EndTime = DateTime.Now;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Dynamically changes the degree of parallelism.
    /// </summary>
    /// <param name="newValue"></param>
    /// <exception cref="ArgumentException"></exception>
    public virtual Task ChangeDegreeOfParallelism(int newValue) =>
        // This can be 0 because we can use 0 dop as a pausing system
        newValue < 0 || newValue > MaxDegreeOfParallelism
            ? throw new ArgumentException($"Must be within 0 and {MaxDegreeOfParallelism}", nameof(newValue))
            : Task.CompletedTask;

    /// <summary>
    /// An awaitable handler that completes when the <see cref="Status"/> is <see cref="ParallelizerStatus.Idle"/>.
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async Task WaitCompletion(CancellationToken cancellationToken = default)
    {
        // Fast-path: if idle, return immediately
        if (Status == ParallelizerStatus.Idle) return;

        // Wait with exponential backoff to reduce wakeups under long waits
        var delay = 50;
        while (Status != ParallelizerStatus.Idle)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = Math.Min(delay * 2, 500);
        }
    }
    #endregion

    #region Protected Methods
    /// <summary>
    /// Whether the CPM is limited to a certain amount (for throttling purposes).
    /// </summary>
    protected bool IsCPMLimited() => CPMLimit > 0 && CPM > CPMLimit;

    /// <summary>
    /// Updates the CPM (safe to be called from multiple threads).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected void UpdateCPM(long nowTicks)
    {
        // Attempt to update CPM without blocking; if another thread is updating, skip this tick
        if (!Monitor.TryEnter(cpmLock))
        {
            return;
        }

        try
        {
            const int windowMs = 60_000;

            // Dequeue timestamps older than the time window
            while (checkedTimestamps.TryPeek(out var timestamp) && nowTicks - timestamp >= windowMs)
            {
                checkedTimestamps.TryDequeue(out _);
            }

            CPM = checkedTimestamps.Count;
        }
        finally
        {
            Monitor.Exit(cpmLock);
        }
    }

    /// <summary>
    ///
    /// </summary>
    public void Dispose()
    {
        try
        {
            softCTS?.Dispose();
        }
        catch { }
        try
        {
            hardCTS?.Dispose();
        }
        catch { }

        _updateTimer?.Dispose();
        stopwatch?.Stop();
        Status = ParallelizerStatus.Idle;
    }
    #endregion
}
