using System;

namespace Flux.Shared.Models;

public record NotificationDto(string Topic, string Message, string Severity, DateTime Timestamp);
