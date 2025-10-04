using System;

namespace OpenBullet2.Shared.Models;

public record UserDto(int Id, string Username, string Roles, DateTime CreatedAt, DateTime? LastLoginAt);
