using System.Threading;
using System.Threading.Tasks;
using OpenBullet2.Shared.Models;

namespace OpenBullet2.Shared.Abstractions;

public interface ISettingsFacade
{
    Task<SettingsSnapshotDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<SettingsSnapshotDto> UpdateAsync(UpdateSettingsRequest request, CancellationToken cancellationToken = default);
}
