using Flux.Core.Entities;

namespace Flux.Core.Repositories;

/// <summary>
/// Stores jobs.
/// </summary>
public interface IJobRepository : IRepository<JobEntity>
{
    /// <summary>
    /// Deletes all jobs from the repository.
    /// </summary>
    void Purge();
}
