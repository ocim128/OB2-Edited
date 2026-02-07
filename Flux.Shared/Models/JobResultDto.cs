using System;

namespace Flux.Shared.Models;

public record JobResultDto(int JobId, string Type, string Data, string Capture, string Proxy, DateTime Timestamp);
