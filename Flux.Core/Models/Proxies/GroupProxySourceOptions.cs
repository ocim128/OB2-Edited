using Flux.Core.Models.Proxies.Sources;
using Flux.Core.Repositories;

namespace Flux.Core.Models.Proxies;

/// <summary>
/// Options for a <see cref="GroupProxySource"/>
/// </summary>
public class GroupProxySourceOptions : ProxySourceOptions
{
    /// <summary>
    /// The ID of the proxy group, as stored in the <see cref="IProxyGroupRepository"/>.
    /// </summary>
    public int GroupId { get; set; } = -1;
}
