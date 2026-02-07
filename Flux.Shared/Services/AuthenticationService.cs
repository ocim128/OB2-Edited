using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Flux.Core.Entities;
using Flux.Core.Repositories;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;
using Flux.Shared.Security;

namespace Flux.Shared.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly IUserRepository _users;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(IUserRepository users, ILogger<AuthenticationService> logger)
    {
        _users = users;
        _logger = logger;
    }

    public async Task<UserDto> EnsureSeedUserAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _users.FindByUsernameAsync(request.Username, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return ToDto(existing);
        }

        _logger.LogInformation("Seeding default user {Username}", request.Username);
        return await RegisterAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _users.FindByUsernameAsync(request.Username, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            throw new InvalidOperationException($"Username '{request.Username}' is already registered");
        }

        var (hash, salt) = PasswordHasher.HashPassword(request.Password);
        var entity = new UserEntity
        {
            Username = request.Username,
            PasswordHash = hash,
            PasswordSalt = salt,
            Roles = request.Roles
        };

        await _users.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("User {Username} registered", request.Username);
        return ToDto(entity);
    }

    public async Task<UserDto?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _users.FindByUsernameAsync(request.Username, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            _logger.LogWarning("Login failed for {Username}: user not found", request.Username);
            return null;
        }

        if (!PasswordHasher.Verify(request.Password, entity.PasswordHash, entity.PasswordSalt))
        {
            _logger.LogWarning("Login failed for {Username}: invalid password", request.Username);
            return null;
        }

        entity.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("User {Username} authenticated", request.Username);
        return ToDto(entity);
    }

    public async Task<UserDto?> GetUserAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _users.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return entity is null ? null : ToDto(entity);
    }

    public Task<UserDto?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
        => GetUserByUsernameInternalAsync(username, cancellationToken);

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<UserDto>();
        await foreach (var entity in _users.GetAll().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            list.Add(ToDto(entity));
        }

        return list;
    }

    public async Task SetLastLoginAsync(int userId, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var entity = await _users.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        entity.LastLoginAt = timestamp;
        await _users.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UserDto?> GetUserByUsernameInternalAsync(string username, CancellationToken cancellationToken)
    {
        var entity = await _users.FindByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        return entity is null ? null : ToDto(entity);
    }

    private static UserDto ToDto(UserEntity entity)
        => new(entity.Id, entity.Username, entity.Roles, entity.CreatedAt, entity.LastLoginAt);
}
