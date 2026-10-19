using Flux.Core.Entities;
using RuriLib.Models.Proxies;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Flux.Core.Repositories;

/// <summary>
/// Stores proxies.
/// </summary>
public interface IProxyRepository : IRepository<ProxyEntity>
{
    /// <summary>
    /// Removes duplicate proxies that belong to the group with a given <paramref name="groupId"/> from the Proxies table.
    /// Duplication is checked on type, host, port, username and password.
    /// Returns the number of removed entries.
    /// </summary>
    Task<int> RemoveDuplicatesAsync(int groupId);

    /// <summary>
    /// Writes the result of a proxy check without reading the entity first.
    /// Used by proxy check jobs, which update one row per checked proxy.
    /// </summary>
    Task<int> UpdateCheckResultAsync(
        int proxyId,
        string country,
        DateTime lastChecked,
        int ping,
        ProxyWorkingStatus status,
        CancellationToken cancellationToken = default);
}
