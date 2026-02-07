using Flux.Core.Entities;
using RuriLib.Models.Proxies;

namespace Flux.Core.Models.Proxies;

/// <summary>
/// Factory that creates a <see cref="Proxy"/> from a <see cref="ProxyEntity"/>.
/// </summary>
public class ProxyFactory
{
    /// <summary>
    /// Creates a <see cref="Proxy"/> from a <see cref="ProxyEntity"/>.
    /// </summary>
    public static Proxy FromEntity(ProxyEntity entity) 
        => new(entity.Host, entity.Port, entity.Type, entity.Username, entity.Password)
    {
        Id = entity.Id,
        Country = entity.Country,
        WorkingStatus = entity.Status,
        LastChecked = entity.LastChecked,
        Ping = entity.Ping
    };
}
