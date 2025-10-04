using System;

namespace OpenBullet2.Shared.Models;

public record NotificationDto(string Topic, string Message, string Severity, DateTime Timestamp);
