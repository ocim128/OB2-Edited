using Flux.Core.Models.Proxies;
using Flux.Web.Attributes;

namespace Flux.Web.Dtos.Job.ProxyCheck;

/// <summary>
/// Information about where to save proxy check results.
/// </summary>
public class ProxyCheckOutputOptionsDto : PolyDto
{
}

/// <summary>
/// Saves proxy check results to the database.
/// </summary>
[PolyType("databaseProxyCheckOutput")]
[MapsFrom(typeof(DatabaseProxyCheckOutputOptions))]
[MapsTo(typeof(DatabaseProxyCheckOutputOptions))]
public class DatabaseProxyCheckOutputOptionsDto : ProxyCheckOutputOptionsDto
{
}
