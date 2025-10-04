using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenBullet2.Shared.Models;

namespace OpenBullet2.Shared.Abstractions;

public interface IPluginService
{
    Task<IReadOnlyList<PluginDescriptorDto>> GetPluginsAsync(CancellationToken cancellationToken = default);
    Task ReloadAsync(CancellationToken cancellationToken = default);
}
