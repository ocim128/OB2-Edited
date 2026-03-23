using System;
using RuriLib.Models.Configs;
using RuriLib.Models.Proxies;

namespace Flux.Shared.Models;

public record JobRuntimeResultDto(
    string Id,
    string Type,
    string Data,
    string Capture,
    string Proxy,
    ProxyType? ProxyType,
    DateTime Timestamp,
    ConfigMode? ConfigMode,
    bool HasBotLog);
