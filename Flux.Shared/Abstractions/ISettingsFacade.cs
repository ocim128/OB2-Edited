using System.Threading;
using System.Threading.Tasks;
using Flux.Shared.Models;

namespace Flux.Shared.Abstractions;

public interface ISettingsFacade
{
    Task<SettingsSnapshotDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<SettingsSnapshotDto> UpdateAsync(UpdateSettingsRequest request, CancellationToken cancellationToken = default);
}
