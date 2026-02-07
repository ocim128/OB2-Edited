using Flux.Core.Entities;

namespace Flux.Core.Repositories;

/// <summary>
/// Stores records to a database.
/// </summary>
public class DbRecordRepository : DbRepository<RecordEntity>, IRecordRepository
{
    public DbRecordRepository(ApplicationDbContext context)
        : base(context)
    {

    }
}
