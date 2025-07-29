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
                semaphore.Release();
                return true;
            }

            return false;
        }

        private bool ShouldApplyCPMLimit()
        {
            if (++cpmCheckCounter < 50) return false;

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

            _ = taskFunction.Invoke(item)
                .ContinueWith(_ => semaphore?.Release())
                .ConfigureAwait(false);
        }

        private async Task WaitForCompletion()
        {
            while (Progress < 1 && !hardCTS.IsCancellationRequested)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }

        private async Task HandleCancellation()
        {
            while (semaphore?.CurrentCount < degreeOfParallelism && !hardCTS.IsCancellationRequested)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }

        private void Cleanup()
        {
            OnCompleted();
            Status = ParallelizerStatus.Idle;
            hardCTS?.Dispose();
            softCTS?.Dispose();
            semaphore?.Dispose();
            semaphore = null;
            stopwatch?.Stop();
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
