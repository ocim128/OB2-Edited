using System;

namespace RuriLib.Models.Diagnostics;

public sealed class BlockPerformanceMetric
{
    public string BlockKey { get; }
    public string Scope { get; }
    public int BlockIndex { get; }
    public string BlockId { get; }
    public string BlockLabel { get; }
    public int ExecutionCount { get; private set; }
    public TimeSpan TotalElapsed { get; private set; }
    public TimeSpan MaxElapsed { get; private set; }
    public double AverageElapsedMs => ExecutionCount == 0 ? 0 : TotalElapsed.TotalMilliseconds / ExecutionCount;

    public BlockPerformanceMetric(string blockKey, string scope, int blockIndex, string blockId, string blockLabel)
    {
        BlockKey = blockKey ?? throw new ArgumentNullException(nameof(blockKey));
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        BlockIndex = blockIndex;
        BlockId = blockId ?? string.Empty;
        BlockLabel = blockLabel ?? string.Empty;
    }

    internal void Record(TimeSpan elapsed)
    {
        ExecutionCount++;
        TotalElapsed += elapsed;

        if (elapsed > MaxElapsed)
        {
            MaxElapsed = elapsed;
        }
    }

    internal void MergeFrom(BlockPerformanceMetric other)
    {
        ArgumentNullException.ThrowIfNull(other);

        ExecutionCount += other.ExecutionCount;
        TotalElapsed += other.TotalElapsed;

        if (other.MaxElapsed > MaxElapsed)
        {
            MaxElapsed = other.MaxElapsed;
        }
    }
}
