using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Parallelization
{
    /// <summary>
    /// High-performance parallelizer that uses fire-and-forget tasks with atomic counters
    /// for maximum throughput.
    /// </summary>
    public class TaskBasedParallelizer<TInput, TOutput> : Parallelizer<TInput, TOutput>
    {
        private SemaphoreSlim _semaphore;
        private int _activeTaskCount;

        /// <inheritdoc/>
        public TaskBasedParallelizer(IEnumerable<TInput> workItems, Func<TInput, CancellationToken, Task<TOutput>> workFunction,
            int degreeOfParallelism, long totalAmount, int skip = 0, int maxDegreeOfParallelism = 200)
            : base(workItems, workFunction, degreeOfParallelism, totalAmount, skip, maxDegreeOfParallelism)
        {
        }

        /// <inheritdoc/>
        public async override Task Start()
        {
            await base.Start().ConfigureAwait(false);

            Status = ParallelizerStatus.Running;
            _ = Task.Run(RunAsync).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async override Task Pause()
        {
            await base.Pause().ConfigureAwait(false);

            // To pause, we just wait for all current tasks to finish by acquiring all semaphore slots.
            for (var i = 0; i < degreeOfParallelism; i++)
            {
                await _semaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            Status = ParallelizerStatus.Paused;
        }

        /// <inheritdoc/>
        public async override Task Resume()
        {
            await base.Resume().ConfigureAwait(false);

            // To resume, we release all the acquired slots.
            _semaphore.Release(degreeOfParallelism);
            Status = ParallelizerStatus.Running;
        }

        /// <inheritdoc/>
        public async override Task Stop()
        {
            await base.Stop().ConfigureAwait(false);

            Status = ParallelizerStatus.Stopping;
            softCTS.Cancel();
            await WaitForTasksCompletion().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async override Task Abort()
        {
            await base.Abort().ConfigureAwait(false);

            Status = ParallelizerStatus.Stopping;
            hardCTS.Cancel();
            softCTS.Cancel();
            await WaitForTasksCompletion().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async override Task ChangeDegreeOfParallelism(int newValue)
        {
            await base.ChangeDegreeOfParallelism(newValue).ConfigureAwait(false);

            var diff = newValue - degreeOfParallelism;
            degreeOfParallelism = newValue;

            if (diff > 0)
            {
                _semaphore.Release(diff);
            }
            else if (diff < 0)
            {
                for (var i = 0; i < -diff; i++)
                {
                    await _semaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private async Task RunAsync()
        {
            _semaphore = new SemaphoreSlim(degreeOfParallelism, MaxDegreeOfParallelism);
            _activeTaskCount = 0;

            try
            {
                using var enumerator = workItems.Skip(skip).GetEnumerator();
                while (enumerator.MoveNext())
                {
                    if (softCTS.IsCancellationRequested) break;

                    await _semaphore.WaitAsync(softCTS.Token).ConfigureAwait(false);

                    var item = enumerator.Current;
                    _ = ProcessItemAsync(item);
                }

                await WaitForTasksCompletion().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (softCTS.IsCancellationRequested)
            {
                // Expected cancellation, swallow it
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

        private async Task ProcessItemAsync(TInput item)
        {
            Interlocked.Increment(ref _activeTaskCount);
            try
            {
                await taskFunction(item).ConfigureAwait(false);
            }
            catch (Exception) when (hardCTS.IsCancellationRequested || softCTS.IsCancellationRequested)
            {
                // Swallow expected cancellations
            }
            finally
            {
                _semaphore.Release();
                Interlocked.Decrement(ref _activeTaskCount);
            }
        }

        private async Task WaitForTasksCompletion()
        {
            // Wait for all tasks to complete with exponential backoff
            var delay = 10;
            while (Interlocked.CompareExchange(ref _activeTaskCount, 0, 0) > 0)
            {
                if (hardCTS.IsCancellationRequested) break;
                await Task.Delay(delay).ConfigureAwait(false);
                delay = Math.Min(delay * 2, 100);
            }
        }

        private void Cleanup()
        {
            try { OnCompleted(); } catch { /* Ignore */ }
            Status = ParallelizerStatus.Idle;
            try { hardCTS?.Dispose(); } catch { /* Ignore */ }
            try { softCTS?.Dispose(); } catch { /* Ignore */ }
            try { _semaphore?.Dispose(); } catch { /* Ignore */ }
            _semaphore = null;
        }
    }
}
