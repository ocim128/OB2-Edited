using System;

namespace OpenBullet2.Shared.Models;

public record JobResultDto(int JobId, string Type, string Data, string Capture, string Proxy, DateTime Timestamp);
