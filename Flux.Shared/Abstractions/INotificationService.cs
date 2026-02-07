using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Flux.Shared.Models;

namespace Flux.Shared.Abstractions;

public interface INotificationService
{
    IAsyncEnumerable<NotificationDto> StreamAsync(CancellationToken cancellationToken = default);
    Task PublishAsync(NotificationDto notification, CancellationToken cancellationToken = default);
}
