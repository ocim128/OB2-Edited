using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RuriLib.Parallelization;
using Xunit;

namespace RuriLib.Tests.Parallelization;

public class TaskBasedParallelizerCpmTests
{
    [Fact]
    public async Task Run_WithoutCpmLimit_ProcessesAllItems()
    {
        var processed = 0;
        var items = Enumerable.Range(0, 50).ToList();

        using var parallelizer = new TaskBasedParallelizer<int, int>(
            items,
            (item, _) =>
            {
                Interlocked.Increment(ref processed);
                return Task.FromResult(item);
            },
            degreeOfParallelism: 5,
            totalAmount: items.Count);

        await parallelizer.Start();
        await parallelizer.WaitCompletion();

        Assert.Equal(items.Count, processed);
    }

    [Fact]
    public async Task Run_WithCpmLimit_ThrottlesInsteadOfIgnoringTheLimit()
    {
        // The default parallelizer used to ignore CPMLimit entirely, so a job configured with
        // a MaximumCPM ran unthrottled. A limit of 60 CPM means the second batch of work must
        // wait for the 60-second sliding window, which we detect via a short observation window
        // rather than by running for a full minute.
        var processed = 0;
        var items = Enumerable.Range(0, 500).ToList();

        using var parallelizer = new TaskBasedParallelizer<int, int>(
            items,
            (item, _) =>
            {
                Interlocked.Increment(ref processed);
                return Task.FromResult(item);
            },
            degreeOfParallelism: 4,
            totalAmount: items.Count);

        // A very low cap: once CPM exceeds it, the dispatcher must stop pulling work.
        parallelizer.CPMLimit = 1;

        await parallelizer.Start();

        // Give the dispatcher time to pass the cap and then block.
        await Task.Delay(1500);

        var processedAtCap = Volatile.Read(ref processed);

        // If the limit were ignored this would race to the full 500 within the same window.
        Assert.InRange(processedAtCap, 1, 50);
        Assert.True(processedAtCap < items.Count, "CPM limit was ignored: all work items ran");

        await parallelizer.Stop();
        parallelizer.Dispose();
    }

    [Fact]
    public async Task Stop_InterruptsAThrottledRun()
    {
        var processed = 0;
        var items = Enumerable.Range(0, 500).ToList();

        using var parallelizer = new TaskBasedParallelizer<int, int>(
            items,
            (item, _) =>
            {
                Interlocked.Increment(ref processed);
                return Task.FromResult(item);
            },
            degreeOfParallelism: 2,
            totalAmount: items.Count);

        parallelizer.CPMLimit = 1;

        await parallelizer.Start();
        await Task.Delay(500);

        // Stop is only legal while running; if the run already drained, there is nothing to cancel.
        if (parallelizer.Status != ParallelizerStatus.Running)
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        await parallelizer.Stop();
        sw.Stop();

        // Stop must cancel the throttle wait, not hang until the CPM window resets.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"Stop took {sw.Elapsed}, the CPM throttle wait was not cancelled");
        Assert.True(Volatile.Read(ref processed) < items.Count);
    }
}
