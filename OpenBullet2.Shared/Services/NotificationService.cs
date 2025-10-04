using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenBullet2.Shared.Abstractions;
using OpenBullet2.Shared.Models;

namespace OpenBullet2.Shared.Services;

public class NotificationService : INotificationService
{
    private readonly Channel<NotificationDto> _channel;
    private readonly ConcurrentQueue<NotificationDto> _buffer = new();
    private readonly ILogger<NotificationService> _logger;
    private const int BufferSize = 100;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
        _channel = Channel.CreateUnbounded<NotificationDto>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    public async Task PublishAsync(NotificationDto notification, CancellationToken cancellationToken = default)
    {
        _buffer.Enqueue(notification);
        while (_buffer.Count > BufferSize && _buffer.TryDequeue(out _))
        {
        }

        _logger.LogDebug("Notification published: {Topic} - {Message}", notification.Topic, notification.Message);
        await _channel.Writer.WriteAsync(notification, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<NotificationDto> StreamAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var notification in _buffer)
        {
            yield return notification;
        }

        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var notification))
            {
                yield return notification;
            }
        }
    }
}
