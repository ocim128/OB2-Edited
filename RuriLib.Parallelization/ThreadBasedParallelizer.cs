using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Parallelization
{
    /// <summary>
    /// Parallelizer that expoits a custom pool of threads.
    /// </summary>
    public class ThreadBasedParallelizer<TInput, TOutput> : Parallelizer<TInput, TOutput>
    {
        #region Private Fields
        private readonly ConcurrentQueue<TInput> workQueue = new();
        private readonly Thread[] workerThreads;
        private volatile bool shouldStop = false;
        private volatile bool isPaused = false;
        private readonly ManualResetEventSlim pauseEvent = new(true);
        private int activeThreads = 0;
        private int cpmCheckCounter = 0;
        #endregion

        #region Constructors
        /// <inheritdoc/>
        public ThreadBasedParallelizer(IEnumerable<TInput> workItems, Func<TInput, CancellationToken, Task<TOutput>> workFunction,
            int degreeOfParallelism, long totalAmount, int skip = 0, int maxDegreeOfParallelism = 200)
            : base(workItems, workFunction, degreeOfParallelism, totalAmount, skip, maxDegreeOfParallelism)
        {
            workerThreads = new Thread[maxDegreeOfParallelism];
        }
        #endregion

        #region Public Methods
        /// <inheritdoc/>
        public async override Task Start()
        {
            await base.Start();

            shouldStop = false;
            isPaused = false;
            activeThreads = 0;
            cpmCheckCounter = 0;
            pauseEvent.Set();
            
            // Start worker threads
            for (int i = 0; i < degreeOfParallelism; i++)
            {
                StartWorkerThread(i);
            }

            stopwatch.Restart();
            Status = ParallelizerStatus.Running;
            _ = Task.Run(ProduceWork).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async override Task Pause()
        {
            await base.Pause();

            Status = ParallelizerStatus.Pausing;
            isPaused = true;
            pauseEvent.Reset();
            Status = ParallelizerStatus.Paused;
            stopwatch.Stop();
        }

        /// <inheritdoc/>
        public async override Task Resume()
        {
            await base.Resume();

            isPaused = false;
            pauseEvent.Set();
            Status = ParallelizerStatus.Running;
            stopwatch.Start();
        }

        /// <inheritdoc/>
        public async override Task Stop()
        {
            await base.Stop();

            Status = ParallelizerStatus.Stopping;
            shouldStop = true;
            pauseEvent.Set(); // Unblock paused threads
            softCTS.Cancel();
            await WaitCompletion().ConfigureAwait(false);
            stopwatch.Stop();
        }

        /// <inheritdoc/>
        public async override Task Abort()
        {
            await base.Abort();

            Status = ParallelizerStatus.Stopping;
            shouldStop = true;
            pauseEvent.Set(); // Unblock paused threads
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

            var oldDOP = degreeOfParallelism;
            degreeOfParallelism = newValue;

            if (newValue > oldDOP)
            {
                // Start additional worker threads
                for (int i = oldDOP; i < newValue; i++)
                {
                    StartWorkerThread(i);
                }
            }
            else if (newValue < oldDOP)
            {
                // Stop excess worker threads - they will exit naturally when work is done
                for (int i = newValue; i < oldDOP; i++)
                {
                    if (workerThreads[i]?.IsAlive == true)
                    {
                        // Threads will check degreeOfParallelism and exit if their index >= new DOP
                    }
                }
            }
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

                while (items.MoveNext() && !softCTS.IsCancellationRequested)
                {
                    // CPM throttling with reduced checking frequency
                    if (++cpmCheckCounter >= 100 && IsCPMLimited())
                    {
                        cpmCheckCounter = 0;
                        await Task.Delay(50, softCTS.Token); // Reduced delay
                        continue;
                    }

                    // Queue work item
                    workQueue.Enqueue(items.Current);

                    // Micro-delay to prevent CPU spinning when queue is full
                    if (workQueue.Count > degreeOfParallelism * 2)
                    {
                        await Task.Delay(1, softCTS.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            finally
            {
                shouldStop = true;
                pauseEvent.Set();
                
                // Wait for all worker threads to finish
                await WaitCurrentWorkCompletion();
                
                OnCompleted();
                Status = ParallelizerStatus.Idle;
                hardCTS?.Dispose();
                softCTS?.Dispose();
                pauseEvent?.Dispose();
                stopwatch?.Stop();
            }
        }

        // Start a worker thread at the specified index
        private void StartWorkerThread(int threadIndex)
        {
            if (threadIndex >= workerThreads.Length || workerThreads[threadIndex]?.IsAlive == true)
                return;

            var thread = new Thread(() => WorkerThreadLoop(threadIndex))
            {
                IsBackground = true,
                Name = $"OB2-Worker-{threadIndex}"
            };
            
            workerThreads[threadIndex] = thread;
            thread.Start();
        }

        // Optimized worker thread loop with producer-consumer pattern
        private void WorkerThreadLoop(int threadIndex)
        {
            Interlocked.Increment(ref activeThreads);
            
            try
            {
                while (!shouldStop && threadIndex < degreeOfParallelism)
                {
                    // Handle pause state efficiently
                    if (isPaused)
                    {
                        pauseEvent.Wait(softCTS.Token);
                        continue;
                    }

                    // Try to get work from queue
                    if (workQueue.TryDequeue(out TInput workItem))
                    {
                        try
                        {
                            // Execute work synchronously (ThreadBased should be sync)
                            if (!softCTS.IsCancellationRequested)
                            {
                                taskFunction(workItem).ConfigureAwait(false).GetAwaiter().GetResult();
                            }
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            // Work function handles its own errors through base class
                        }
                    }
                    else
                    {
                        // No work available - micro-sleep to prevent CPU spinning
                        Thread.Sleep(1);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during cancellation
            }
            finally
            {
                Interlocked.Decrement(ref activeThreads);
                workerThreads[threadIndex] = null;
            }
        }

        // Wait until the current work completion
        private async Task WaitCurrentWorkCompletion()
        {
            while (activeThreads > 0)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
        }
        #endregion
    }
}
