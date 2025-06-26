using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Parallelization
{
    /// <summary>
    /// Advanced parallelizer that uses optimized Parallel.ForEachAsync with full feature support.
    /// </summary>
    public class ParallelBasedParallelizer<TInput, TOutput> : Parallelizer<TInput, TOutput>
    {
        #region Private Fields
        private CancellationTokenSource parallelCTS;
        private volatile bool isPaused = false;
        private volatile bool shouldStop = false;
        private readonly ManualResetEventSlim pauseEvent = new(true);
        private int savedDOP;
        private int cpmCheckCounter = 0;
        private readonly ConcurrentQueue<TInput> workQueue = new();
        private volatile bool isProducerFinished = false;
        private int activeTasks = 0;
        #endregion

        #region Constructors
        /// <inheritdoc/>
        public ParallelBasedParallelizer(IEnumerable<TInput> workItems, Func<TInput, CancellationToken, Task<TOutput>> workFunction,
            int degreeOfParallelism, long totalAmount, int skip = 0, int maxDegreeOfParallelism = 200)
            : base(workItems, workFunction, degreeOfParallelism, totalAmount, skip, maxDegreeOfParallelism)
        {
            parallelCTS = new CancellationTokenSource();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the number of currently active parallel tasks.
        /// </summary>
        public int CurrentTasks => activeTasks;

        /// <summary>
        /// Gets the number of items currently waiting in the queue.
        /// </summary>
        public int QueuedTasks => workQueue?.Count ?? 0;
        #endregion

        #region Public Methods
        /// <inheritdoc/>
        public async override Task Start()
        {
            await base.Start().ConfigureAwait(false);

            // Initialize state
            isPaused = false;
            shouldStop = false;
            isProducerFinished = false;
            activeTasks = 0;
            cpmCheckCounter = 0;
            savedDOP = degreeOfParallelism;
            
            parallelCTS?.Dispose();
            parallelCTS = new CancellationTokenSource();
            pauseEvent.Set();

            stopwatch.Restart();
            Status = ParallelizerStatus.Running;
            
            // Start producer and consumer tasks
            _ = Task.Run(ProduceWork).ConfigureAwait(false);
            _ = Task.Run(() => ConsumeWork()).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async override Task Pause()
        {
            await base.Pause().ConfigureAwait(false);

            Status = ParallelizerStatus.Pausing;
            isPaused = true;
            pauseEvent.Reset();
            Status = ParallelizerStatus.Paused;
            stopwatch.Stop();
        }

        /// <inheritdoc/>
        public async override Task Resume()
        {
            await base.Resume().ConfigureAwait(false);

            isPaused = false;
            pauseEvent.Set();
            Status = ParallelizerStatus.Running;
            stopwatch.Start();
        }

        /// <inheritdoc/>
        public async override Task Stop()
        {
            await base.Stop().ConfigureAwait(false);

            Status = ParallelizerStatus.Stopping;
            shouldStop = true;
            pauseEvent.Set(); // Unblock paused tasks
            softCTS.Cancel();
            await WaitCompletion().ConfigureAwait(false);
            stopwatch.Stop();
        }

        /// <inheritdoc/>
        public async override Task Abort()
        {
            await base.Abort().ConfigureAwait(false);

            Status = ParallelizerStatus.Stopping;
            shouldStop = true;
            pauseEvent.Set(); // Unblock paused tasks
            parallelCTS.Cancel();
            hardCTS.Cancel();
            softCTS.Cancel();
            await WaitCompletion().ConfigureAwait(false);
            stopwatch.Stop();
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

            // For running state, update the degree and let the consumer adapt
            var oldDOP = degreeOfParallelism;
            degreeOfParallelism = newValue;
            
            // The consumer will automatically adapt to the new DOP
            // No need to restart parallel execution
        }
        #endregion

        #region Private Methods
        // Producer method - feeds work items into the queue
        private async void ProduceWork()
        {
            try
            {
                // Skip the items
                using var items = workItems.Skip(skip).GetEnumerator();

                while (items.MoveNext() && !shouldStop && !softCTS.IsCancellationRequested)
                {
                    // Handle pause state
                    if (isPaused)
                    {
                        await Task.Run(() => pauseEvent.Wait(softCTS.Token), softCTS.Token).ConfigureAwait(false);
                        continue;
                    }

                    // CPM throttling with reduced checking frequency
                    if (++cpmCheckCounter >= 50 && IsCPMLimited())
                    {
                        cpmCheckCounter = 0;
                        await Task.Delay(50, softCTS.Token).ConfigureAwait(false);
                        continue;
                    }

                    // Queue work item
                    workQueue.Enqueue(items.Current);
                    
                    // Throttle if queue gets too large
                    if (workQueue.Count > degreeOfParallelism * 3)
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
                isProducerFinished = true;
            }
        }

        // Consumer method - uses optimized Parallel.ForEachAsync with advanced features
        private async void ConsumeWork()
        {
            try
            {
                while (!shouldStop && (!isProducerFinished || !workQueue.IsEmpty))
                {
                    // Handle pause state
                    if (isPaused)
                    {
                        await Task.Run(() => pauseEvent.Wait(softCTS.Token), softCTS.Token).ConfigureAwait(false);
                        continue;
                    }

                    // Collect a batch of work items for parallel processing
                    var batch = new List<TInput>();
                    var batchSize = Math.Min(degreeOfParallelism * 2, 100);
                    
                    for (int i = 0; i < batchSize && workQueue.TryDequeue(out TInput item); i++)
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
                        parallelCTS.Token, softCTS.Token, hardCTS.Token);

                    var options = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = degreeOfParallelism,
                        TaskScheduler = TaskScheduler.Default,
                        CancellationToken = combinedCTS.Token
                    };

                    await Parallel.ForEachAsync(batch, options, async (item, token) =>
                    {
                        Interlocked.Increment(ref activeTasks);
                        try
                        {
                            if (!shouldStop && !isPaused && !token.IsCancellationRequested)
                            {
                                await taskFunction(item).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            Interlocked.Decrement(ref activeTasks);
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
                while (activeTasks > 0)
                {
                    await Task.Delay(10).ConfigureAwait(false);
                }

                OnCompleted();
                Status = ParallelizerStatus.Idle;
                hardCTS?.Dispose();
                softCTS?.Dispose();
                parallelCTS?.Dispose();
                pauseEvent?.Dispose();
                stopwatch?.Stop();
            }
        }
        #endregion
    }
}