using System;
using System.Runtime.CompilerServices;

namespace RuriLib.Http;

/// <summary>
/// Configuration class for HTTP performance optimizations and tuning parameters.
/// </summary>
public static class HttpPerformanceConfig
{
    // Connection pooling settings
    public static int MaxConnectionsPerHost { get; set; } = 10;
    public static int ConnectionTimeoutMinutes { get; set; } = 5;
    public static int PoolCleanupIntervalMinutes { get; set; } = 2;
    
    // Client pooling settings
    public static int MaxClientsPerKey { get; set; } = 8;
    public static int ClientTimeoutMinutes { get; set; } = 3;
    public static int ClientCleanupIntervalMinutes { get; set; } = 2;
    
    // Memory pool settings
    public static int MaxStringBuilderCapacity { get; set; } = 8192;
    public static int MaxHeaderDictionarySize { get; set; } = 64;
    public static int MaxStringListSize { get; set; } = 128;
    public static int MaxMemoryStreamCapacity { get; set; } = 1024 * 1024; // 1MB
    
    // Buffer settings
    public static int DefaultBufferSize { get; set; } = 8192;
    public static int MaxBufferSize { get; set; } = 64 * 1024; // 64KB
    public static int InitialStringBuilderCapacity { get; set; } = 256;
    public static int InitialHeaderDictionaryCapacity { get; set; } = 16;
    
    // Performance tuning
    public static bool EnableAggressiveOptimization { get; set; } = true;
    public static bool EnableConnectionReuse { get; set; } = true;
    public static bool EnableHeaderCaching { get; set; } = true;
    public static bool EnableMemoryPooling { get; set; } = true;
    public static bool EnableFastHeaderParsing { get; set; } = true;
    
    // Timeout settings
    public static TimeSpan DefaultReceiveTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public static TimeSpan DefaultSendTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public static TimeSpan DefaultConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    
    // Advanced settings
    public static bool EnableTcpNoDelay { get; set; } = true;
    public static bool EnableTcpKeepAlive { get; set; } = true;
    public static int TcpKeepAliveTime { get; set; } = 7200; // 2 hours in seconds
    public static int TcpKeepAliveInterval { get; set; } = 75; // seconds
    public static int TcpKeepAliveRetryCount { get; set; } = 9;
    
    // HTTP/2 settings (for future use)
    public static bool EnableHttp2 { get; set; } = false;
    public static int Http2MaxConcurrentStreams { get; set; } = 100;
    public static int Http2InitialWindowSize { get; set; } = 65535;
    
    // Compression settings
    public static bool EnableAutomaticDecompression { get; set; } = true;
    public static bool EnableGzipCompression { get; set; } = true;
    public static bool EnableDeflateCompression { get; set; } = true;
    public static bool EnableBrotliCompression { get; set; } = true;
    
    // Security settings
    public static bool EnableCertificateValidation { get; set; } = true;
    public static bool EnableSslSessionReuse { get; set; } = true;
    public static bool EnableTls13 { get; set; } = true;
    
    // Monitoring and diagnostics
    public static bool EnablePerformanceCounters { get; set; } = false;
    public static bool EnableDetailedLogging { get; set; } = false;
    public static bool EnableRequestResponseLogging { get; set; } = false;
    
    /// <summary>
    /// Applies high-performance preset configuration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyHighPerformancePreset()
    {
        MaxConnectionsPerHost = 15;
        ConnectionTimeoutMinutes = 3;
        PoolCleanupIntervalMinutes = 1;
        
        MaxClientsPerKey = 12;
        ClientTimeoutMinutes = 2;
        ClientCleanupIntervalMinutes = 1;
        
        DefaultBufferSize = 16384; // 16KB
        MaxBufferSize = 128 * 1024; // 128KB
        
        EnableAggressiveOptimization = true;
        EnableConnectionReuse = true;
        EnableHeaderCaching = true;
        EnableMemoryPooling = true;
        EnableFastHeaderParsing = true;
        
        DefaultReceiveTimeout = TimeSpan.FromSeconds(20);
        DefaultSendTimeout = TimeSpan.FromSeconds(20);
        DefaultConnectTimeout = TimeSpan.FromSeconds(5);
        
        EnableTcpNoDelay = true;
        EnableTcpKeepAlive = true;
        TcpKeepAliveTime = 3600; // 1 hour
        TcpKeepAliveInterval = 30;
        
        EnableAutomaticDecompression = true;
        EnableSslSessionReuse = true;
        EnableTls13 = true;
    }
    
    /// <summary>
    /// Applies memory-optimized preset configuration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyMemoryOptimizedPreset()
    {
        MaxConnectionsPerHost = 5;
        ConnectionTimeoutMinutes = 2;
        PoolCleanupIntervalMinutes = 1;
        
        MaxClientsPerKey = 3;
        ClientTimeoutMinutes = 1;
        ClientCleanupIntervalMinutes = 1;
        
        MaxStringBuilderCapacity = 4096;
        MaxHeaderDictionarySize = 32;
        MaxStringListSize = 64;
        MaxMemoryStreamCapacity = 512 * 1024; // 512KB
        
        DefaultBufferSize = 4096; // 4KB
        MaxBufferSize = 32 * 1024; // 32KB
        InitialStringBuilderCapacity = 128;
        InitialHeaderDictionaryCapacity = 8;
        
        EnableMemoryPooling = true;
        EnableHeaderCaching = true;
        EnableFastHeaderParsing = true;
    }
    
    /// <summary>
    /// Applies balanced preset configuration (default).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyBalancedPreset()
    {
        MaxConnectionsPerHost = 10;
        ConnectionTimeoutMinutes = 5;
        PoolCleanupIntervalMinutes = 2;
        
        MaxClientsPerKey = 8;
        ClientTimeoutMinutes = 3;
        ClientCleanupIntervalMinutes = 2;
        
        DefaultBufferSize = 8192; // 8KB
        MaxBufferSize = 64 * 1024; // 64KB
        
        EnableAggressiveOptimization = true;
        EnableConnectionReuse = true;
        EnableHeaderCaching = true;
        EnableMemoryPooling = true;
        EnableFastHeaderParsing = true;
        
        DefaultReceiveTimeout = TimeSpan.FromSeconds(30);
        DefaultSendTimeout = TimeSpan.FromSeconds(30);
        DefaultConnectTimeout = TimeSpan.FromSeconds(10);
        
        EnableTcpNoDelay = true;
        EnableTcpKeepAlive = true;
        EnableAutomaticDecompression = true;
        EnableSslSessionReuse = true;
    }
    
    /// <summary>
    /// Validates the current configuration and throws if invalid.
    /// </summary>
    public static void ValidateConfiguration()
    {
        if (MaxConnectionsPerHost <= 0)
            throw new ArgumentException("MaxConnectionsPerHost must be greater than 0");
            
        if (ConnectionTimeoutMinutes <= 0)
            throw new ArgumentException("ConnectionTimeoutMinutes must be greater than 0");
            
        if (MaxClientsPerKey <= 0)
            throw new ArgumentException("MaxClientsPerKey must be greater than 0");
            
        if (DefaultBufferSize <= 0)
            throw new ArgumentException("DefaultBufferSize must be greater than 0");
            
        if (MaxBufferSize < DefaultBufferSize)
            throw new ArgumentException("MaxBufferSize must be greater than or equal to DefaultBufferSize");
            
        if (DefaultReceiveTimeout <= TimeSpan.Zero)
            throw new ArgumentException("DefaultReceiveTimeout must be greater than zero");
            
        if (DefaultSendTimeout <= TimeSpan.Zero)
            throw new ArgumentException("DefaultSendTimeout must be greater than zero");
            
        if (DefaultConnectTimeout <= TimeSpan.Zero)
            throw new ArgumentException("DefaultConnectTimeout must be greater than zero");
    }
}