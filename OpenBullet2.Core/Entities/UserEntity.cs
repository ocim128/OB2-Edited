using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenBullet2.Core.Entities;

/// <summary>
/// Represents an authenticated user of OpenBullet 2.
/// </summary>
[Table("Users")]
public class UserEntity : Entity
{
    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(512)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string PasswordSalt { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Roles { get; set; } = "User";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    public bool IsLocked { get; set; } = false;
}
