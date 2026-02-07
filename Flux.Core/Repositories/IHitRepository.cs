using Flux.Core.Entities;
using System.Threading.Tasks;

namespace Flux.Core.Repositories;

/// <summary>
/// Stores hits.
/// </summary>
public interface IHitRepository : IRepository<HitEntity>
{
    /// <summary>
    /// Deletes all hits from the repository.
    /// </summary>
    Task PurgeAsync();

    /// <summary>
    /// Count the number of hits.
    /// </summary>
    Task<long> CountAsync();
}
