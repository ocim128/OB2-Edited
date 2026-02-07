using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Flux.Shared.Models;

namespace Flux.Shared.Abstractions;

public interface IPluginService
{
    Task<IReadOnlyList<PluginDescriptorDto>> GetPluginsAsync(CancellationToken cancellationToken = default);
    Task ReloadAsync(CancellationToken cancellationToken = default);
}
