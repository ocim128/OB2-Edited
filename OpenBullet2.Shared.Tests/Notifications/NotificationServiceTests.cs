using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenBullet2.Shared.Models;
using OpenBullet2.Shared.Services;

namespace OpenBullet2.Shared.Tests.Notifications;

public sealed class NotificationServiceTests
{
    private readonly NotificationService _service = new(NullLogger<NotificationService>.Instance);

    [Fact]
    public async Task PublishAsync_BuffersAndStreamsNotifications()
    {
        var first = new NotificationDto("jobs", "Job started", "info", DateTime.UtcNow);
        var second = new NotificationDto("hits", "Hit captured", "success", DateTime.UtcNow);

        await _service.PublishAsync(first);
        await _service.PublishAsync(second);

        var received = new List<NotificationDto>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await foreach (var notification in _service.StreamAsync(cts.Token))
        {
            received.Add(notification);
            if (received.Count == 2)
            {
                break;
            }
        }

        received.Should().ContainInOrder(first, second);
    }
}
