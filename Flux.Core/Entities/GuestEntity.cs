using System;
using System.Collections.Generic;

namespace Flux.Core.Entities;

/// <summary>
/// This entity stores a guest user of Flux.
/// </summary>
public class GuestEntity : Entity
{
    /// <summary>
    /// The username that the guest uses to log in.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The bcrypt hash of the password of the guest.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// The time when access will expire for this guest.
    /// </summary>
    public DateTime AccessExpiration { get; set; }

    /// <summary>
    /// A comma-separated list of IPv4 or IPv6 addresses that the guest
    /// is allowed to use when connecting to the remote instance of Flux.
    /// These can include masked IP ranges and static DNS.
    /// </summary>
    public string AllowedAddresses { get; set; }

    /// <summary>
    /// The proxy groups that the guest owns.
    /// </summary>
    public ICollection<ProxyGroupEntity> ProxyGroups { get; set; }
}
