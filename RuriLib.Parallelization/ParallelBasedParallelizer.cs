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
    private BlockingCollection<TInput> _workQueue;
    private Task _producerTask;
    private Task _consumerTask;
    #endregion

    #region Public Methods
    /// <inheritdoc/>
    public override async Task Start()
    {
        await base.Start().ConfigureAwait(false);

        _workQueue = new BlockingCollection<TInput>();

        Status = ParallelizerStatus.Running;

        // Start producer and consumer tasks
        _producerTask = Task.Run(ProduceWorkAsync, softCTS.Token);
        _consumerTask = Task.Run(ConsumeWorkAsync, softCTS.Token);

        _ = _consumerTask.ContinueWith(t => Cleanup(), TaskScheduler.Default);
    }

    /// <inheritdoc/>
    public override async Task Pause()
    {
        await base.Pause().ConfigureAwait(false);
        Status = ParallelizerStatus.Paused;
    }

    /// <inheritdoc/>
    public override async Task Resume()
    {
        await base.Resume().ConfigureAwait(false);
        Status = ParallelizerStatus.Running;
    }

    /// <inheritdoc/>
    public override async Task Stop()
    {
        await base.Stop().ConfigureAwait(false);

        Status = ParallelizerStatus.Stopping;
        softCTS.Cancel();
        _workQueue.CompleteAdding();
        await Task.WhenAll(_producerTask, _consumerTask).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task Abort()
    {
        await base.Abort().ConfigureAwait(false);

        Status = ParallelizerStatus.Stopping;
        hardCTS.Cancel();
        softCTS.Cancel();
        _workQueue.CompleteAdding();
        await Task.WhenAll(_producerTask, _consumerTask).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async override Task ChangeDegreeOfParallelism(int newValue)
    {
        await base.ChangeDegreeOfParallelism(newValue).ConfigureAwait(false);
        degreeOfParallelism = newValue;
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Producer method - feeds work items into the queue
    /// </summary>
    private async Task ProduceWorkAsync()
    {
        try
        {
            using var enumerator = workItems.Skip(skip).GetEnumerator();
            while (!softCTS.IsCancellationRequested && enumerator.MoveNext())
            {
                await pauseToken.WaitWhilePausedAsync(softCTS.Token).ConfigureAwait(false);

                if (IsCPMLimited())
                {
                    await Task.Delay(100, softCTS.Token).ConfigureAwait(false);
                }

                _workQueue.Add(enumerator.Current, softCTS.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
        finally
        {
            _workQueue.CompleteAdding();
        }
    }

    /// <summary>
    /// Consumer method - uses optimized Parallel.ForEachAsync with advanced features
    /// </summary>
    private async Task ConsumeWorkAsync()
    {
        var po = new ParallelOptions
        {
            MaxDegreeOfParallelism = degreeOfParallelism,
            CancellationToken = hardCTS.Token
        };

        try
        {
            await Parallel.ForEachAsync(_workQueue.GetConsumingEnumerable(hardCTS.Token), po,
                async (item, token) =>
            {
                await pauseToken.WaitWhilePausedAsync(token).ConfigureAwait(false);
                await taskFunction(item).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
    }

    private void Cleanup()
    {
        if (Status == ParallelizerStatus.Idle) return;

        FinalizeRun();
        OnCompleted();
        Status = ParallelizerStatus.Idle;
        hardCTS?.Dispose();
        softCTS?.Dispose();
        _workQueue?.Dispose();
    }
    #endregion
}
