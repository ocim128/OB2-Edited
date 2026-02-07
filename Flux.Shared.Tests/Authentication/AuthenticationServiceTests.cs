using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Flux.Core;
using Flux.Core.Repositories;
using Flux.Shared.Models;
using Flux.Shared.Security;
using Flux.Shared.Services;

namespace Flux.Shared.Tests.Authentication;

public sealed class AuthenticationServiceTests : IAsyncLifetime
{
    private readonly ApplicationDbContext _context;
    private readonly DbUserRepository _repository;
    private readonly AuthenticationService _service;

    public AuthenticationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"auth-tests-{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new DbUserRepository(_context);
        _service = new AuthenticationService(_repository, NullLogger<AuthenticationService>.Instance);
    }

    [Fact]
    public async Task RegisterAsync_PersistsUserWithHashedPassword()
    {
        var request = new RegisterRequest("operator", "P@ssw0rd!", "Admin");

        var created = await _service.RegisterAsync(request);

        created.Username.Should().Be("operator");
        created.Roles.Should().Be("Admin");

        var entity = await _repository.FindByUsernameAsync("operator");
        entity.Should().NotBeNull();
        entity!.PasswordHash.Should().NotBe(request.Password);
        entity.PasswordSalt.Should().NotBeNullOrWhiteSpace();
        PasswordHasher.Verify(request.Password, entity.PasswordHash, entity.PasswordSalt).Should().BeTrue();
    }

    [Fact]
    public async Task LoginAsync_ReturnsNullForInvalidCredentials()
    {
        await _service.RegisterAsync(new RegisterRequest("alice", "ValidPass!1", "Admin"));

        var user = await _service.LoginAsync(new LoginRequest("alice", "wrong"));

        user.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_ReturnsUserForValidCredentials()
    {
        await _service.RegisterAsync(new RegisterRequest("bob", "ValidPass!1", "User"));

        var user = await _service.LoginAsync(new LoginRequest("bob", "ValidPass!1"));

        user.Should().NotBeNull();
        user!.Username.Should().Be("bob");
        user.Roles.Should().Be("User");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() { _context.Dispose(); return Task.CompletedTask; }
}

