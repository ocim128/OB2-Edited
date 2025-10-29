namespace RuriLib.Http;

public static class HttpPerformanceConfig
{
    public static int MaxConnectionsPerHost { get; set; } = 10;
    public static int ConnectionTimeoutMinutes { get; set; } = 5;
    public static int PoolCleanupIntervalMinutes { get; set; } = 2;
}