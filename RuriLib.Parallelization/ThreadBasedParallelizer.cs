using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Parallelization
{
    /// <summary>
    /// Parallelizer that exploits a custom pool of threads with a blocking collection for high efficiency.
    /// </summary>
    public class ThreadBasedParallelizer<TInput, TOutput> : Parallelizer<TInput, TOutput>
    {
        private BlockingCollection<TInput> _workQueue;
        private Thread[] _workerThreads;
        private CancellationTokenSource[] _workerCTS;

        /// <inheritdoc/>
        public ThreadBasedParallelizer(IEnumerable<TInput> workItems, Func<TInput, CancellationToken, Task<TOutput>> workFunction,
            int degreeOfParallelism, long totalAmount, int skip = 0, int maxDegreeOfParallelism = 200)
            : base(workItems, workFunction, degreeOfParallelism, totalAmount, skip, maxDegreeOfParallelism)
        {
        }

        /// <inheritdoc/>
        public async override Task Start()
        {
            await base.Start().ConfigureAwait(false);

            _workQueue = new BlockingCollection<TInput>(new ConcurrentQueue<TInput>());
            _workerThreads = new Thread[MaxDegreeOfParallelism];
            _workerCTS = new CancellationTokenSource[MaxDegreeOfParallelism];

            for (var i = 0; i < degreeOfParallelism; i++)
            {
                _workerCTS[i] = new CancellationTokenSource();
                var token = _workerCTS[i].Token;
                _workerThreads[i] = new Thread(() => WorkerLoop(token))
                {
                    IsBackground = true,
                    Name = $"OB2-Worker-{i}"
                };
                _workerThreads[i].Start();
            }

            Status = ParallelizerStatus.Running;
            _ = Task.Run(ProduceWorkAsync).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async override Task Pause()
        {
            await base.Pause().ConfigureAwait(false);
            Status = ParallelizerStatus.Paused;
        }

        /// <inheritdoc/>
        public async override Task Resume()
        {
            await base.Resume().ConfigureAwait(false);
            Status = ParallelizerStatus.Running;
        }

        /// <inheritdoc/>
        public async override Task Stop()
        {
            await base.Stop().ConfigureAwait(false);

            Status = ParallelizerStatus.Stopping;
            softCTS.Cancel();
            _workQueue.CompleteAdding();
            await WaitForThreadsCompletion().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async override Task Abort()
        {
            await base.Abort().ConfigureAwait(false);

            Status = ParallelizerStatus.Stopping;
            hardCTS.Cancel();
            softCTS.Cancel();
            _workQueue.CompleteAdding();
            await WaitForThreadsCompletion().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override Task ChangeDegreeOfParallelism(int newValue)
        {
            var oldDOP = degreeOfParallelism;
            base.ChangeDegreeOfParallelism(newValue).Wait();
            degreeOfParallelism = newValue;

            if (newValue > oldDOP)
            {
                // Start new threads
                for (var i = oldDOP; i < newValue; i++)
                {
                    if (_workerThreads[i]?.IsAlive == true) continue;

                    _workerCTS[i] = new CancellationTokenSource();
                    var token = _workerCTS[i].Token;
                    _workerThreads[i] = new Thread(() => WorkerLoop(token)) { IsBackground = true, Name = $"OB2-Worker-{i}" };
                    _workerThreads[i].Start();
                }
            }
            else if (newValue < oldDOP)
            {
                // Stop surplus threads
                for (var i = newValue; i < oldDOP; i++)
                {
                    _workerCTS[i]?.Cancel();
                }
            }

            return Task.CompletedTask;
        }

        private async Task ProduceWorkAsync()
        {
            try
            {
                using var enumerator = workItems.Skip(skip).GetEnumerator();
                while (!softCTS.IsCancellationRequested && enumerator.MoveNext())
                {
                    // Wait if paused
                    await pauseToken.WaitWhilePausedAsync().ConfigureAwait(false);
                    _workQueue.Add(enumerator.Current, softCTS.Token);
                }
            }
            catch (OperationCanceledException) when (softCTS.IsCancellationRequested)
            {
                // Expected cancellation
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
            finally
            {
                _workQueue.CompleteAdding();
                await WaitForThreadsCompletion().ConfigureAwait(false);
                Cleanup();
            }
        }

        private void WorkerLoop(CancellationToken token)
        {
            using var linkedCTS = CancellationTokenSource.CreateLinkedTokenSource(hardCTS.Token, token);

            try
            {
                foreach (var item in _workQueue.GetConsumingEnumerable(linkedCTS.Token))
                {
                    // Wait if paused
                    pauseToken.WaitWhilePausedAsync().GetAwaiter().GetResult();

                    taskFunction(item).ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }
            catch (OperationCanceledException) when (linkedCTS.IsCancellationRequested)
            {
                // Expected cancellation
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }

        private async Task WaitForThreadsCompletion()
        {
            var maxDopEver = _workerThreads.Count(t => t != null);
            for (var i = 0; i < maxDopEver; i++)
            {
                if (_workerThreads[i]?.IsAlive == true)
                {
                    await Task.Run(() => _workerThreads[i].Join()).ConfigureAwait(false);
                }
            }
        }

        private void Cleanup()
        {
            if (Status == ParallelizerStatus.Idle) return;

            try { OnCompleted(); } catch { /* Ignore */ }
            Status = ParallelizerStatus.Idle;
            try { hardCTS?.Dispose(); } catch { /* Ignore */ }
            try { softCTS?.Dispose(); } catch { /* Ignore */ }
            try { _workQueue?.Dispose(); } catch { /* Ignore */ }

            if (_workerCTS is not null)
            {
                foreach (var cts in _workerCTS)
                {
                    try { cts?.Dispose(); } catch { /* Ignore */ }
                }
            }
        }
    }
}
