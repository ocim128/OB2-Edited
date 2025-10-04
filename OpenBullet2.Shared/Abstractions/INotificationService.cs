using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenBullet2.Shared.Models;

namespace OpenBullet2.Shared.Abstractions;

public interface INotificationService
{
    IAsyncEnumerable<NotificationDto> StreamAsync(CancellationToken cancellationToken = default);
    Task PublishAsync(NotificationDto notification, CancellationToken cancellationToken = default);
}
