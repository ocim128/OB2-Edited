#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Flux.Core.Entities;

namespace Flux.Core.Repositories;

public interface IUserRepository : IRepository<UserEntity>
{
    Task<UserEntity?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);
}

