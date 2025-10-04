using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenBullet2.Shared.Models;

namespace OpenBullet2.Shared.Abstractions;

public interface IAuthenticationService
{
    Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<UserDto> EnsureSeedUserAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<UserDto?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<UserDto?> GetUserAsync(int id, CancellationToken cancellationToken = default);
    Task<UserDto?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task SetLastLoginAsync(int userId, DateTime timestamp, CancellationToken cancellationToken = default);
}
