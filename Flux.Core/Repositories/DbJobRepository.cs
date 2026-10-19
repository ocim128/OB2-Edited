using Microsoft.EntityFrameworkCore;
using Flux.Core.Entities;
using Flux.Core.Extensions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Flux.Core.Repositories;

/// <summary>
/// Stores jobs to a database.
/// </summary>
public class DbJobRepository : DbRepository<JobEntity>, IJobRepository
{
    public DbJobRepository(ApplicationDbContext context)
        : base(context)
    {

    }

    public override async Task<JobEntity> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        // The previous implementation paid for an Include(Owner) join and then discarded it with
        // ReloadAsync (which only refreshes scalars), so the join and a second SELECT were both
        // wasted and it could still NRE when the job did not exist.
        // AsNoTracking is consistent with this repository's UpdateAsync, which already detaches
        // before marking Modified, and it keeps the result fresh instead of returning a cached
        // tracked instance. Callers that need the owner use the OwnerId column.
        return await context.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async override Task UpdateAsync(JobEntity entity, CancellationToken cancellationToken = default)
    {
        context.DetachLocal<JobEntity>(entity.Id);
        context.Entry(entity).State = EntityState.Modified;
        await base.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async override Task UpdateAsync(IEnumerable<JobEntity> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            context.DetachLocal<JobEntity>(entity.Id);
            context.Entry(entity).State = EntityState.Modified;
        }

        await base.UpdateAsync(entities, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Purge() => context.Database.ExecuteSqlRaw($"DELETE FROM {nameof(ApplicationDbContext.Jobs)}");
}
