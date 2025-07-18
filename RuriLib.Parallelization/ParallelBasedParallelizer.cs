using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Parallelization;

/// <summary>
/// Advanced parallelizer that uses optimized Parallel.ForEachAsync with full feature support.
/// </summary>
/// <inheritdoc/>
public class ParallelBasedParallelizer<TInput, TOutput>(IEnumerable<TInput> workItems, Func<TInput, CancellationToken, Task<TOutput>> workFunction,
    int degreeOfParallelism, long totalAmount, int skip = 0, int maxDegreeOfParallelism = 200) : Parallelizer<TInput, TOutput>(workItems, workFunction, degreeOfParallelism, totalAmount, skip, maxDegreeOfParallelism)
{
    #region Private Fields
    private CancellationTokenSource _parallelCTS = new();
    private volatile bool _isPaused;
    private volatile bool _shouldStop;
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    private int _savedDOP;
    private int _cpmCheckCounter;
    private readonly ConcurrentQueue<TInput> _workQueue = new();
    private volatile bool _isProducerFinished;
    private int _activeTasks;

    #endregion

    #region Public Properties
    /// <summary>
    /// Gets the number of currently active parallel tasks.
    /// </summary>
    public int CurrentTasks => _activeTasks;

    /// <summary>
    /// Gets the number of items currently waiting in the queue.
    /// </summary>
    public int QueuedTasks => _workQueue?.Count ?? 0;
    #endregion

    #region Public Methods
    /// <inheritdoc/>
    public override async Task Start()
    {
        await base.Start().ConfigureAwait(false);

        // Initialize state
        _isPaused = false;
        _shouldStop = false;
        _isProducerFinished = false;
        _activeTasks = 0;
        _cpmCheckCounter = 0;
        _savedDOP = degreeOfParallelism;

        _parallelCTS?.Dispose();
        _parallelCTS = new CancellationTokenSource();
        _pauseEvent.Set();

        stopwatch.Restart();
        Status = ParallelizerStatus.Running;

        // Start producer and consumer tasks
        _ = Task.Run(ProduceWork).ConfigureAwait(false);
        _ = Task.Run(() => ConsumeWork()).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task Pause()
    {
        await base.Pause().ConfigureAwait(false);

        Status = ParallelizerStatus.Pausing;
        _isPaused = true;
        _pauseEvent.Reset();
        Status = ParallelizerStatus.Paused;
        stopwatch.Stop();
    }

    /// <inheritdoc/>
    public override async Task Resume()
    {
        await base.Resume().ConfigureAwait(false);

        _isPaused = false;
        _pauseEvent.Set();
        Status = ParallelizerStatus.Running;
        stopwatch.Start();
    }

    /// <inheritdoc/>
    public override async Task Stop()
    {
        await base.Stop().ConfigureAwait(false);

        Status = ParallelizerStatus.Stopping;
        _shouldStop = true;
        _pauseEvent.Set(); // Unblock paused tasks
        softCTS.Cancel();
        await WaitCompletion().ConfigureAwait(false);
        stopwatch.Stop();
    }

    /// <inheritdoc/>
    public override async Task Abort()
    {
        await base.Abort().ConfigureAwait(false);

        Status = ParallelizerStatus.Stopping;
        _shouldStop = true;
        _pauseEvent.Set(); // Unblock paused tasks
        _parallelCTS.Cancel();
        hardCTS.Cancel();
        softCTS.Cancel();
        await WaitCompletion().ConfigureAwait(false);
        stopwatch.Stop();
    }

    /// <inheritdoc/>
    public override async Task ChangeDegreeOfParallelism(int newValue)
    {
        await base.ChangeDegreeOfParallelism(newValue);

        if (Status == ParallelizerStatus.Idle)
        {
            degreeOfParallelism = newValue;
            return;
        }
        else if (Status == ParallelizerStatus.Paused)
        {
            _savedDOP = newValue;
            return;
        }

        // For running state, update the degree and let the consumer adapt
        degreeOfParallelism = newValue;

        // The consumer will automatically adapt to the new DOP
        // No need to restart parallel execution
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Producer method - feeds work items into the queue
    /// </summary>
    private async void ProduceWork()
    {
        try
        {
            // Skip the items
            using var items = workItems.Skip(skip).GetEnumerator();

            while (items.MoveNext() && !_shouldStop && !softCTS.IsCancellationRequested)
            {
                // Handle pause state
                if (_isPaused)
                {
                    await Task.Run(() => _pauseEvent.Wait(softCTS.Token), softCTS.Token).ConfigureAwait(false);
                    continue;
                }

                // CPM throttling with reduced checking frequency
                if (++_cpmCheckCounter >= 50 && IsCPMLimited())
                {
                    _cpmCheckCounter = 0;
                    await Task.Delay(50, softCTS.Token).ConfigureAwait(false);
                    continue;
                }

                // Queue work item
                _workQueue.Enqueue(items.Current);

                // Throttle if queue gets too large
                if (_workQueue.Count > degreeOfParallelism * 3)
                {
                    await Task.Delay(1, softCTS.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        finally
        {
            _isProducerFinished = true;
        }
    }

    /// <summary>
    /// Consumer method - uses optimized Parallel.ForEachAsync with advanced features
    /// </summary>
    private async void ConsumeWork()
    {
        try
        {
            while (!_shouldStop && (!_isProducerFinished || !_workQueue.IsEmpty))
            {
                // Handle pause state
                if (_isPaused)
                {
                    await Task.Run(() => _pauseEvent.Wait(softCTS.Token), softCTS.Token).ConfigureAwait(false);
                    continue;
                }

                // Collect a batch of work items for parallel processing
                var batch = new List<TInput>();
                var batchSize = Math.Min(degreeOfParallelism * 2, 100);

                for (var i = 0; i < batchSize && _workQueue.TryDequeue(out var item); i++)
                {
                    batch.Add(item);
                }

                if (batch.Count == 0)
                {
                    // No work available, short delay before checking again
                    await Task.Delay(1, softCTS.Token).ConfigureAwait(false);
                    continue;
                }

                // Process batch using optimized Parallel.ForEachAsync
                var combinedCTS = CancellationTokenSource.CreateLinkedTokenSource(
                    _parallelCTS.Token, softCTS.Token, hardCTS.Token);

                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = degreeOfParallelism,
                    TaskScheduler = TaskScheduler.Default,
                    CancellationToken = combinedCTS.Token
                };

                await Parallel.ForEachAsync(batch, options, async (item, token) =>
                {
                    _ = Interlocked.Increment(ref _activeTasks);
                    try
                    {
                        if (!_shouldStop && !_isPaused && !token.IsCancellationRequested)
                        {
                            await taskFunction(item).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        _ = Interlocked.Decrement(ref _activeTasks);
                    }
                }).ConfigureAwait(false);

                combinedCTS.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
        finally
        {
            // Wait for any remaining active tasks
            while (_activeTasks > 0)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }

            OnCompleted();
            Status = ParallelizerStatus.Idle;
            hardCTS?.Dispose();
            softCTS?.Dispose();
            _parallelCTS?.Dispose();
            _pauseEvent?.Dispose();
            stopwatch?.Stop();
        }
    }
    #endregion
}