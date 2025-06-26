using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Parallelization
{
    /// <summary>
    /// Parallelizer that expoits batches of multiple tasks and the WaitAll function.
    /// </summary>
    public class TaskBasedParallelizer<TInput, TOutput> : Parallelizer<TInput, TOutput>
    {
        #region Private Fields
        private int BatchSize => Math.Max(degreeOfParallelism * 3, 32); // Dynamic batch size
        private SemaphoreSlim semaphore;
        private ConcurrentQueue<TInput> queue;
        private int savedDOP;
        private volatile bool dopDecreaseRequested;
        private int cpmLimitDelayMs = 50;
        private int cpmCheckCounter = 0; // More efficient than DateTime.Now

        private readonly object queueLock = new object(); // For bulk operations
        private int adaptiveBatchSize; // Adaptive batch sizing for performance
        #endregion

        #region Constructors
        /// <inheritdoc/>
        public TaskBasedParallelizer(IEnumerable<TInput> workItems, Func<TInput, CancellationToken, Task<TOutput>> workFunction,
            int degreeOfParallelism, long totalAmount, int skip = 0, int maxDegreeOfParallelism = 200)
            : base(workItems, workFunction, degreeOfParallelism, totalAmount, skip, maxDegreeOfParallelism)
        {

        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Gets or sets the delay in milliseconds when CPM is limited.
        /// </summary>
        public int CPMLimitDelayMs 
        {
            get => cpmLimitDelayMs;
            set => cpmLimitDelayMs = Math.Max(10, Math.Min(1000, value));
        }

        /// <summary>
        /// Gets the number of currently running tasks.
        /// </summary>
        public int CurrentTasks => degreeOfParallelism - (semaphore?.CurrentCount ?? 0);

        /// <summary>
        /// Gets the number of tasks currently waiting in the queue.
        /// </summary>
        public int QueuedTasks => queue?.Count ?? 0;

        /// <inheritdoc/>
        public async override Task Start()
        {
            await base.Start().ConfigureAwait(false);

            cpmCheckCounter = 0;
            adaptiveBatchSize = BatchSize; // Initialize adaptive batch size
            stopwatch.Restart();
            Status = ParallelizerStatus.Running;
            _ = Task.Run(Run).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async override Task Pause()
        {
            await base.Pause().ConfigureAwait(false);

            Status = ParallelizerStatus.Pausing;
            savedDOP = degreeOfParallelism;
            await ChangeDegreeOfParallelism(0).ConfigureAwait(false);
            Status = ParallelizerStatus.Paused;
            stopwatch.Stop();
        }

        /// <inheritdoc/>
        public async override Task Resume()
        {
            await base.Resume().ConfigureAwait(false);

            cpmCheckCounter = 0; // Reset CPM check on resume
            Status = ParallelizerStatus.Resuming;
            await ChangeDegreeOfParallelism(savedDOP).ConfigureAwait(false);
            Status = ParallelizerStatus.Running;
            stopwatch.Start();
        }

        /// <inheritdoc/>
        public async override Task Stop()
        {
            await base.Stop().ConfigureAwait(false);

            Status = ParallelizerStatus.Stopping;
            softCTS.Cancel();
            await WaitCompletion().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async override Task Abort()
        {
            await base.Abort().ConfigureAwait(false);

            Status = ParallelizerStatus.Stopping;
            hardCTS.Cancel();
            softCTS.Cancel();
            await WaitCompletion().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async override Task ChangeDegreeOfParallelism(int newValue)
        {
            await base.ChangeDegreeOfParallelism(newValue);

            if (Status == ParallelizerStatus.Idle)
            {
                degreeOfParallelism = newValue;
                return;
            }
            else if (Status == ParallelizerStatus.Paused)
            {
                savedDOP = newValue;
                return;
            }

            if (newValue == degreeOfParallelism)
            {
                return;
            }
            else if (newValue > degreeOfParallelism)
            {
                semaphore.Release(newValue - degreeOfParallelism);
            }
            else
            {
                dopDecreaseRequested = true;
                for (var i = 0; i < degreeOfParallelism - newValue; ++i)
                {
                    if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                        break; // Exit if can't acquire within timeout
                }
                dopDecreaseRequested = false;
            }

            degreeOfParallelism = newValue;
        }
        #endregion

        #region Private Methods
        // Run is executed in fire and forget mode (not awaited)
        private async void Run()
        {
            semaphore = new SemaphoreSlim(degreeOfParallelism, MaxDegreeOfParallelism);
            dopDecreaseRequested = false;

            // Skip the items
            using var items = workItems.Skip(skip).GetEnumerator();

            // Create the queue
            queue = new ConcurrentQueue<TInput>();

            // Track if there are more items to process
            bool hasMoreItems = true;

            // Enqueue the first batch (at most adaptiveBatchSize items) with bulk operation
            var initiallyAdded = FillQueue(items, adaptiveBatchSize);
            if (initiallyAdded < adaptiveBatchSize)
            {
                hasMoreItems = false;
            }

            try
            {
                // While there are items in the queue and we didn't cancel, dequeue one, wait and then
                // queue another task if there are more to queue
                while ((hasMoreItems || !queue.IsEmpty) && !softCTS.IsCancellationRequested)
                {
                    // Wait for the semaphore
                    await semaphore.WaitAsync(softCTS.Token).ConfigureAwait(false);

                    if (softCTS.IsCancellationRequested)
                    {
                        semaphore.Release();
                        break;
                    }

                    if (dopDecreaseRequested)
                    {
                        semaphore.Release();
                        await Task.Delay(cpmLimitDelayMs, softCTS.Token).ConfigureAwait(false);
                        continue;
                    }

                    // Check CPM limit occasionally (every ~50 iterations) for better performance
                    if (++cpmCheckCounter >= 50 && IsCPMLimited())
                    {
                        cpmCheckCounter = 0;
                        semaphore.Release();
                        await Task.Delay(cpmLimitDelayMs, softCTS.Token).ConfigureAwait(false);
                        continue;
                    }

                    // If the current batch is running out, refill it efficiently with adaptive sizing
                    if (queue.Count < (adaptiveBatchSize / 2) && hasMoreItems)
                    {
                        // Adaptive batch size based on current load
                        var targetQueueSize = Math.Min(adaptiveBatchSize, degreeOfParallelism * 4);
                        var itemsToAdd = targetQueueSize - queue.Count;
                        var actuallyAdded = FillQueue(items, itemsToAdd);
                        
                        // Check if we've reached the end of enumeration
                        if (actuallyAdded < itemsToAdd)
                        {
                            hasMoreItems = false;
                        }
                        
                        // Adapt batch size based on queue consumption rate
                        var currentActiveTasks = CurrentTasks;
                        if (currentActiveTasks > degreeOfParallelism * 0.8)
                        {
                            adaptiveBatchSize = Math.Min(adaptiveBatchSize * 2, MaxDegreeOfParallelism * 4);
                        }
                        else if (currentActiveTasks < degreeOfParallelism * 0.3)
                        {
                            adaptiveBatchSize = Math.Max(adaptiveBatchSize / 2, degreeOfParallelism);
                        }
                    }

                    // If we can dequeue an item, run it
                    if (queue.TryDequeue(out TInput item))
                    {
                        // The task will release its slot no matter what (original working pattern)
                        _ = taskFunction.Invoke(item)
                            .ContinueWith(_ => semaphore?.Release())
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        // No work available, release semaphore immediately
                        semaphore.Release();
                        
                        // If no more items and queue is empty, we're done
                        if (!hasMoreItems && queue.IsEmpty)
                        {
                            break;
                        }
                    }
                }

                // Wait for every remaining task from the last batch to finish unless aborted
                while (Progress < 1 && !hardCTS.IsCancellationRequested)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Wait for current tasks to finish unless aborted
                while (semaphore?.CurrentCount < degreeOfParallelism && !hardCTS.IsCancellationRequested)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
            finally
            {
                OnCompleted();
                Status = ParallelizerStatus.Idle;
                hardCTS?.Dispose();
                softCTS?.Dispose();
                semaphore?.Dispose();
                semaphore = null;
                stopwatch?.Stop();
            }
        }



        // Bulk queue filling for better performance
        private int FillQueue(IEnumerator<TInput> items, int maxCount)
        {
            // Use lock-free approach when possible, fall back to lock for bulk operations
            var added = 0;
            while (added < maxCount && items.MoveNext())
            {
                queue.Enqueue(items.Current);
                added++;
            }
            return added;
        }
        #endregion
    }
}
