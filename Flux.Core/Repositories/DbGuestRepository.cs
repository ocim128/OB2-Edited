using Flux.Core.Entities;

namespace Flux.Core.Repositories;

/// <summary>
/// Stores guests to a database.
/// </summary>
public class DbGuestRepository : DbRepository<GuestEntity>, IGuestRepository
{
    public DbGuestRepository(ApplicationDbContext context)
        : base(context)
    {

    }
}
