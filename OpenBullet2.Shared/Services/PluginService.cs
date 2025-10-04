using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenBullet2.Shared.Abstractions;
using OpenBullet2.Shared.Models;
using RuriLib.Services;

namespace OpenBullet2.Shared.Services;

public class PluginService : IPluginService
{
    private readonly PluginRepository _repository;
    private readonly ILogger<PluginService> _logger;

    public PluginService(PluginRepository repository, ILogger<PluginService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task<IReadOnlyList<PluginDescriptorDto>> GetPluginsAsync(CancellationToken cancellationToken = default)
    {
        var descriptors = _repository.GetPlugins()
            .Select(assembly =>
            {
                var name = assembly.GetName();
                return new PluginDescriptorDto(
                    name.Name ?? "Unknown",
                    name.Version?.ToString() ?? "1.0.0.0",
                    assembly.FullName ?? name.Name ?? "Unknown",
                    true);
            })
            .ToList();

        return Task.FromResult((IReadOnlyList<PluginDescriptorDto>)descriptors);
    }

    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        // Trigger enumeration to ensure the repository refreshes descriptors.
        _ = _repository.GetPlugins().ToList();
        _logger.LogInformation("Plugins reloaded");
        return Task.CompletedTask;
    }
}
