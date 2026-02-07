using System;

namespace Flux.Shared.Models;

public record UserDto(int Id, string Username, string Roles, DateTime CreatedAt, DateTime? LastLoginAt);
