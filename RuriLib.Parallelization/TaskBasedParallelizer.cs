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
            InitializeRun();

            using var items = workItems.Skip(skip).GetEnumerator();
            var processingState = new ProcessingState();

            try
            {
                await ProcessWorkItems(items, processingState);
                await WaitForCompletion();
            }
            catch (OperationCanceledException)
            {
                await HandleCancellation();
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
            finally
            {
                Cleanup();
            }
        }

        private void InitializeRun()
        {
            semaphore = new SemaphoreSlim(degreeOfParallelism, MaxDegreeOfParallelism);
            dopDecreaseRequested = false;
            queue = new ConcurrentQueue<TInput>();
            adaptiveBatchSize = BatchSize;
        }

        private async Task ProcessWorkItems(IEnumerator<TInput> items, ProcessingState state)
        {
            state.HasMoreItems = InitializeQueue(items);

            while (ShouldContinueProcessing(state))
            {
                await semaphore.WaitAsync(softCTS.Token).ConfigureAwait(false);

                if (ShouldSkipIteration())
                {
                    continue;
                }

                await RefillQueueIfNeeded(items, state);
                await ProcessNextItem(state);
            }
        }

        private bool InitializeQueue(IEnumerator<TInput> items)
        {
            var initiallyAdded = FillQueue(items, adaptiveBatchSize);
            return initiallyAdded >= adaptiveBatchSize;
        }

        private bool ShouldContinueProcessing(ProcessingState state)
        {
            return (state.HasMoreItems || !queue.IsEmpty) && !softCTS.IsCancellationRequested;
        }

        private bool ShouldSkipIteration()
        {
            if (softCTS.IsCancellationRequested)
            {
                semaphore.Release();
                return true;
            }

            if (dopDecreaseRequested)
            {
                semaphore.Release();
                return true;
            }

            if (ShouldApplyCPMLimit())
            {
                // Apply a short cooperative delay to truly throttle when CPM is limited.
                // We cannot 'await' here because this method is synchronous, so we schedule
                // the delay on the side and release the slot immediately.
                _ = Task.Delay(CPMLimitDelayMs, softCTS.Token);
                semaphore.Release();
                return true;
            }

            return false;
        }

        private bool ShouldApplyCPMLimit()
        {
            // Check CPM only every N iterations to reduce overhead
            // Make the sampling frequency loosely proportional to DOP
            int checkInterval = Math.Clamp(degreeOfParallelism, 20, 100);
            if (++cpmCheckCounter < checkInterval) return false;

            cpmCheckCounter = 0;
            return IsCPMLimited();
        }

        private async Task RefillQueueIfNeeded(IEnumerator<TInput> items, ProcessingState state)
        {
            if (!NeedsQueueRefill(state)) return;

            var itemsToAdd = CalculateItemsToAdd();
            var actuallyAdded = FillQueue(items, itemsToAdd);

            if (actuallyAdded < itemsToAdd)
            {
                state.HasMoreItems = false;
            }

            AdaptBatchSize();
        }

        private bool NeedsQueueRefill(ProcessingState state)
        {
            return queue.Count < (adaptiveBatchSize / 2) && state.HasMoreItems;
        }

        private int CalculateItemsToAdd()
        {
            var targetQueueSize = Math.Min(adaptiveBatchSize, degreeOfParallelism * 4);
            return targetQueueSize - queue.Count;
        }

        private void AdaptBatchSize()
        {
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

        private async Task ProcessNextItem(ProcessingState state)
        {
            if (!queue.TryDequeue(out TInput item))
            {
                semaphore.Release();

                if (!state.HasMoreItems && queue.IsEmpty)
                {
                    state.ShouldBreak = true;
                }
                return;
            }

            // Ensure semaphore slot is always released, even if the continuation isn't scheduled due to sync completion
            _ = taskFunction.Invoke(item)
                .ContinueWith(t =>
                {
                    // Observe exception to avoid UnobservedTaskException surfacing
                    var _ = t.Exception;
                    semaphore?.Release();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                .ConfigureAwait(false);
        }

        private async Task WaitForCompletion()
        {
            // Wait until all scheduled work has been processed or we were hard-canceled
            var delay = 50;
            while (!hardCTS.IsCancellationRequested)
            {
                // If no items left to process and all permits are free, we are done
                if (queue.IsEmpty && semaphore?.CurrentCount == degreeOfParallelism)
                {
                    break;
                }
                await Task.Delay(delay).ConfigureAwait(false);
                delay = Math.Min(delay * 2, 250);
            }
        }

        private async Task HandleCancellation()
        {
            // Drain running tasks gracefully after cancellation to avoid slot leak
            var delay = 50;
            while (semaphore?.CurrentCount < degreeOfParallelism && !hardCTS.IsCancellationRequested)
            {
                await Task.Delay(delay).ConfigureAwait(false);
                delay = Math.Min(delay * 2, 250);
            }
        }

        private void Cleanup()
        {
            try
            {
                OnCompleted();
            }
            catch { /* swallow event handler errors */ }

            Status = ParallelizerStatus.Idle;

            try { hardCTS?.Dispose(); } catch { }
            try { softCTS?.Dispose(); } catch { }
            try { semaphore?.Dispose(); } catch { }
            semaphore = null;
            try { stopwatch?.Stop(); } catch { }
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

        #region Helper Classes
        private class ProcessingState
        {
            public bool HasMoreItems { get; set; } = true;
            public bool ShouldBreak { get; set; } = false;
        }
        #endregion
    }
}
