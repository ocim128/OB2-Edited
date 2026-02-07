using Flux.Core.Entities;
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
}
