namespace RuriLib.Parallelization;

/// <summary>
/// The types of parallelizing techniques available.
/// </summary>
public enum ParallelizerType
{
    /// <summary>
    /// Uses tasks to parallelize work.
    /// </summary>
    TaskBased,

    /// <summary>
    /// Uses Parallel.ForEachAsync to parallelize work.
    /// </summary>
    ParallelBased
}
