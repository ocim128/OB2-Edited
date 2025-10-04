#nullable enable
using System.Threading;
using System.Threading.Tasks;
using OpenBullet2.Core.Entities;

namespace OpenBullet2.Core.Repositories;

public interface IUserRepository : IRepository<UserEntity>
{
    Task<UserEntity?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);
}

