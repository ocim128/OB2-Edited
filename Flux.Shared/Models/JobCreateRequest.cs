namespace Flux.Shared.Models;

public record JobCreateRequest(
    string Name,
    string ConfigId,
    int Bots,
    int WordlistId,
    bool AutoStart = false,
    string ProxyMode = "Default",
    bool ShuffleProxies = true,
    bool NeverBanProxies = false,
    bool ConcurrentProxyMode = false,
    int PeriodicReloadIntervalSeconds = 0,
    bool MarkAsToCheckOnAbort = false,
    int Skip = 0);
