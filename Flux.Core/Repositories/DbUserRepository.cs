#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Flux.Core.Entities;

namespace Flux.Core.Repositories;

public class DbUserRepository : DbRepository<UserEntity>, IUserRepository
{
    public DbUserRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public Task<UserEntity?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
        => GetAll().FirstOrDefaultAsync(user => user.Username == username, cancellationToken);
}

