using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using RuriLib.Proxies;
using RuriLib.Proxies.Clients;
using RuriLib.Proxies.Exceptions;
using System.Collections.Generic;
using RuriLib.Http.Models;
using System.Linq;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace RuriLib.Http;

/// <summary>
/// High-performance custom implementation of an HttpClient with advanced connection pooling and memory optimization.
/// </summary>
/// <remarks>
/// Creates a new instance of <see cref="RLHttpClient"/> given a <paramref name="proxyClient"/>.
/// If <paramref name="proxyClient"/> is null, <see cref="NoProxyClient"/> will be used.
/// </remarks>
public class RLHttpClient(ProxyClient proxyClient = null) : IDisposable
{
    // High-performance connection pool with concurrent access using configuration
    private static readonly ConcurrentDictionary<string, ConnectionPoolEntry> _connectionPool = new();
    private static Timer _poolCleanupTimer;

    // Memory optimization with ArrayPool
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

    // Current connection state
    private DateTime lastUsed = DateTime.UtcNow;

    // Connection pool entry for efficient reuse
    private sealed class ConnectionPoolEntry
    {
        public ConcurrentQueue<PooledConnection> Connections { get; } = new();
        public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
        public int ActiveConnections;
        public SemaphoreSlim SlotAvailable { get; } = new(
            HttpPerformanceConfig.MaxConnectionsPerHost,
            HttpPerformanceConfig.MaxConnectionsPerHost);
    }

    private sealed class PooledConnection : IDisposable
    {
        public TcpClient TcpClient { get; set; }
        public Stream CommonStream { get; set; }
        public NetworkStream NetworkStream { get; set; }
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;
        public bool IsSecure { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }

        public bool IsValid => TcpClient?.Connected == true &&
                              (DateTime.UtcNow - LastUsed).TotalMinutes < HttpPerformanceConfig.ConnectionTimeoutMinutes;

        public void Dispose()
        {
            TcpClient?.Close();
            CommonStream?.Dispose();
            NetworkStream?.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CleanupConnectionPool(object state)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-HttpPerformanceConfig.ConnectionTimeoutMinutes);
        var keysToRemove = new List<string>();

        foreach (var kvp in _connectionPool)
        {
            var entry = kvp.Value;

            // Clean up expired connections within the entry
            var validConnections = new List<PooledConnection>();
            while (entry.Connections.TryDequeue(out var conn))
            {
                if (conn.IsValid)
                {
                    validConnections.Add(conn);
                }
                else
                {
                    conn.Dispose();
                    ReleaseConnectionSlot(entry);
                }
            }

            foreach (var conn in validConnections)
            {
                entry.Connections.Enqueue(conn);
            }

            if (entry.LastAccessed < cutoff && Volatile.Read(ref entry.ActiveConnections) == 0)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            if (_connectionPool.TryRemove(key, out var entry))
            {
                while (entry.Connections.TryDequeue(out var conn))
                {
                    conn.Dispose();
                }
            }
        }
    }

    #region Properties
    /// <summary>
    /// The underlying proxy client.
    /// </summary>
    public ProxyClient ProxyClient { get; } = proxyClient ?? new NoProxyClient();

    /// <summary>
    /// Gets the raw bytes of all the requests that were sent.
    /// </summary>
    public List<byte[]> RawRequests { get; } = [];

    /// <summary>
    /// Maximum number of raw requests to keep in memory to prevent memory leaks.
    /// </summary>
    private const int MaxRawRequestsToKeep = 100;

    /// <summary>
    /// Allow automatic redirection on 3xx reply.
    /// </summary>
    public bool AllowAutoRedirect { get; set; } = true;

    /// <summary>
    /// The maximum number of times a request will be redirected.
    /// </summary>
    public int MaxNumberOfRedirects { get; set; } = 8;

    /// <summary>
    /// Whether to read the content of the response. Set to false if you're only interested
    /// in headers.
    /// </summary>
    public bool ReadResponseContent { get; set; } = true;

    /// <summary>
    /// The timeout used while receiving response headers and body bytes.
    /// Zero and <see cref="Timeout.InfiniteTimeSpan"/> disable this internal timeout.
    /// </summary>
    public TimeSpan ReceiveTimeout { get; set; } = HttpPerformanceConfig.DefaultReceiveTimeout;

    /// <summary>
    /// The allowed SSL or TLS protocols.
    /// </summary>
    public SslProtocols SslProtocols { get; set; } = SslProtocols.None;

    /// <summary>
    /// If true, <see cref="AllowedCipherSuites"/> will be used instead of the default ones.
    /// </summary>
    public bool UseCustomCipherSuites { get; set; }

    /// <summary>
    /// The cipher suites to send to the server during the TLS handshake, in order.
    /// The default value of this property contains the cipher suites sent by Firefox as of 21 Dec 2020.
    /// </summary>
    public TlsCipherSuite[] AllowedCipherSuites { get; set; } =
    [
        // Modern 2024 Browser Suites (Chrome/Firefox compatible)
        TlsCipherSuite.TLS_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA,
        TlsCipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA,
        TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA
    ];

    /// <summary>
    /// Gets the type of decompression method used by the handler for automatic
    /// decompression of the HTTP content response.
    /// </summary>
    /// <remarks>
    /// Support GZip and Deflate encoding automatically
    /// </remarks>
    public static DecompressionMethods AutomaticDecompression => DecompressionMethods.GZip | DecompressionMethods.Deflate;

    /// <summary>
    /// Gets or sets delegate to verifies the remote Secure Sockets Layer (SSL)
    /// certificate used for authentication.
    /// </summary>
    public RemoteCertificateValidationCallback ServerCertificateCustomValidationCallback { get; set; }

    /// <summary>
    /// Gets or sets the X509 certificate revocation mode.
    /// </summary>
    public X509RevocationMode CertRevocationMode { get; set; }

    #endregion Properties

    /// <summary>
    /// Sends an HTTP request with high-performance connection pooling and optimized async patterns.
    /// </summary>
    /// <param name="request">The HTTP request to send</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The HTTP response</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public async Task<HttpResponse> SendAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        var redirectCount = 0;
        var currentRequest = request;

        // Pre-allocate for performance
        var redirectHeaders = new Dictionary<string, string>(16, StringComparer.OrdinalIgnoreCase);

        while (redirectCount <= MaxNumberOfRedirects)
        {
            var response = await SendSingleAsync(currentRequest, cancellationToken).ConfigureAwait(false);

            if (!AllowAutoRedirect || !IsRedirectStatusCode(response.StatusCode))
            {
                return response;
            }

            if (++redirectCount > MaxNumberOfRedirects)
            {
                return response;
            }

            if (!response.Headers.TryGetValue("Location", out var locationValues) ||
                locationValues.Count == 0 || string.IsNullOrEmpty(locationValues[0]))
            {
                return response;
            }

            var location = locationValues[0];

            var redirectUri = new Uri(currentRequest.Uri, location);

            var newMethod = GetRedirectMethod(currentRequest.Method, response.StatusCode);
            var preserveContent = ShouldPreserveContent(currentRequest.Method, newMethod, response.StatusCode);

            // Reuse dictionary for better performance
            redirectHeaders.Clear();
            foreach (var header in currentRequest.Headers)
            {
                redirectHeaders[header.Key] = header.Value;
            }

            // Update Host header for new domain
            if (!string.Equals(redirectUri.Host, currentRequest.Uri.Host, StringComparison.OrdinalIgnoreCase))
            {
                redirectHeaders["Host"] = redirectUri.Host;

                // Clear cookies for different domain
                redirectHeaders.Remove("Cookie");
            }

            currentRequest = new HttpRequest
            {
                Uri = redirectUri,
                Method = newMethod,
                Version = currentRequest.Version,
                Headers = new Dictionary<string, string>(redirectHeaders, StringComparer.OrdinalIgnoreCase),
                Cookies = new Dictionary<string, string>(currentRequest.Cookies, StringComparer.OrdinalIgnoreCase),
                AbsoluteUriInFirstLine = currentRequest.AbsoluteUriInFirstLine,
                Content = preserveContent ? currentRequest.Content : null
            };
        }

        return await SendSingleAsync(currentRequest, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.MovedPermanently ||
               statusCode == HttpStatusCode.Found ||
               statusCode == HttpStatusCode.SeeOther ||
               statusCode == HttpStatusCode.TemporaryRedirect ||
               statusCode == HttpStatusCode.PermanentRedirect;
    }

    private async Task<HttpResponse> SendSingleAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var poolKey = GetPoolKey(request.Uri.Host, request.Uri.Port, request.Uri.Scheme == "https");
        var pooledConnection = await GetPooledConnectionAsync(poolKey, request.Uri.Host, request.Uri.Port, request.Uri.Scheme == "https", cancellationToken).ConfigureAwait(false);

        bool returnedToPool = false;
        try
        {
            await SendDataAsync(request, pooledConnection.CommonStream, cancellationToken).ConfigureAwait(false);
            var response = await ReceiveDataAsync(request, pooledConnection.CommonStream, cancellationToken).ConfigureAwait(false);

            if (CanReturnConnectionToPool(request, response, pooledConnection))
            {
                pooledConnection.LastUsed = DateTime.UtcNow;
                returnedToPool = ReturnConnectionToPool(poolKey, pooledConnection);
            }

            return response;
        }
        finally
        {
            if (!returnedToPool)
            {
                pooledConnection.Dispose();
                if (_connectionPool.TryGetValue(poolKey, out var entry))
                {
                    ReleaseConnectionSlot(entry);
                }
            }
        }
    }

    private bool CanReturnConnectionToPool(HttpRequest request, HttpResponse response, PooledConnection connection)
    {
        if (!HttpPerformanceConfig.EnableConnectionReuse || !ReadResponseContent || !connection.IsValid)
        {
            return false;
        }

        if (HasConnectionToken(request.Headers, "close") || HasConnectionToken(response.Headers, "close"))
        {
            return false;
        }

        if (request.Version <= new Version(1, 0) && !HasConnectionToken(request.Headers, "keep-alive"))
        {
            return false;
        }

        if (response.Version <= new Version(1, 0) && !HasConnectionToken(response.Headers, "keep-alive"))
        {
            return false;
        }

        return response.CanReuseConnection;
    }

    private static bool HasConnectionToken(IDictionary<string, string> headers, string token)
    {
        if (headers == null || !headers.TryGetValue("Connection", out var value))
        {
            return false;
        }

        return value.Split(',')
            .Any(part => part.Trim().Equals(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasConnectionToken(IDictionary<string, List<string>> headers, string token)
    {
        if (headers == null || !headers.TryGetValue("Connection", out var values))
        {
            return false;
        }

        return values.SelectMany(v => v.Split(','))
            .Any(part => part.Trim().Equals(token, StringComparison.OrdinalIgnoreCase));
    }

    private string GetPoolKey(string host, int port, bool isSecure)
    {
        var proxyKey = ProxyClient switch
        {
            NoProxyClient => "noproxy",
            _ => string.Join(":",
                ProxyClient.GetType().Name,
                ProxyClient.Settings?.Host ?? string.Empty,
                ProxyClient.Settings?.Port.ToString() ?? string.Empty,
                ProxyClient.Settings?.Credentials?.UserName ?? string.Empty,
                ProxyClient.Settings?.Credentials?.Password ?? string.Empty,
                ProxyClient.Settings?.ConnectTimeout.Ticks.ToString() ?? string.Empty,
                ProxyClient.Settings?.ReadWriteTimeOut.Ticks.ToString() ?? string.Empty)
        };
        var cipherKey = UseCustomCipherSuites && AllowedCipherSuites is { Length: > 0 }
            ? string.Join(",", AllowedCipherSuites.Select(c => c.ToString()))
            : "default";
        return $"{host}:{port}:{isSecure}:{proxyKey}:{(int)SslProtocols}:{cipherKey}:{CertRevocationMode}";
    }

    private static HttpMethod GetRedirectMethod(HttpMethod originalMethod, HttpStatusCode statusCode)
        => statusCode switch
        {
            HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect => originalMethod,
            HttpStatusCode.SeeOther => originalMethod == HttpMethod.Head ? HttpMethod.Head : HttpMethod.Get,
            HttpStatusCode.MovedPermanently or HttpStatusCode.Found => originalMethod == HttpMethod.Post ? HttpMethod.Get : originalMethod,
            _ => HttpMethod.Get
        };

    private static bool ShouldPreserveContent(HttpMethod originalMethod, HttpMethod redirectMethod, HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect ||
           ((statusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found) &&
            redirectMethod == originalMethod &&
            originalMethod != HttpMethod.Get &&
            originalMethod != HttpMethod.Head);

    private async Task<PooledConnection> GetPooledConnectionAsync(string poolKey, string host, int port, bool isSecure, CancellationToken cancellationToken)
    {
        var entry = _connectionPool.GetOrAdd(poolKey, _ => new ConnectionPoolEntry());
        entry.LastAccessed = DateTime.UtcNow;

        while (true)
        {
            // Try to get an existing connection
            while (entry.Connections.TryDequeue(out var connection))
            {
                if (connection.IsValid)
                {
                    connection.LastUsed = DateTime.UtcNow;
                    return connection;
                }

                connection.Dispose();
                ReleaseConnectionSlot(entry);
            }

            // Create new connection if under limit
            if (TryReserveConnectionSlot(entry))
            {
                try
                {
                    return await CreateNewConnectionAsync(host, port, isSecure, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    ReleaseConnectionSlot(entry);
                    throw;
                }
            }

            // Wait efficiently for a slot to become available (replaces spin-wait polling)
            await entry.SlotAvailable.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool TryReserveConnectionSlot(ConnectionPoolEntry entry)
    {
        while (true)
        {
            var current = Volatile.Read(ref entry.ActiveConnections);
            if (current >= HttpPerformanceConfig.MaxConnectionsPerHost)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref entry.ActiveConnections, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    private static void ReleaseConnectionSlot(ConnectionPoolEntry entry)
    {
        while (true)
        {
            var current = Volatile.Read(ref entry.ActiveConnections);
            if (current <= 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref entry.ActiveConnections, current - 1, current) == current)
            {
                // Signal one waiting caller that a slot is now available
                try { entry.SlotAvailable.Release(); } catch (SemaphoreFullException) { }
                return;
            }
        }
    }

    private async Task<PooledConnection> CreateNewConnectionAsync(string host, int port, bool isSecure, CancellationToken cancellationToken)
    {
        var tcpClient = await ProxyClient.ConnectAsync(host, port, null, cancellationToken).ConfigureAwait(false);
        var networkStream = tcpClient.GetStream();
        Stream commonStream = networkStream;

        if (isSecure)
        {
            try
            {
                var sslStream = new SslStream(networkStream, false, ServerCertificateCustomValidationCallback);

                var sslOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    EnabledSslProtocols = SslProtocols == SslProtocols.None 
                        ? (SslProtocols.Tls12 | SslProtocols.Tls13) 
                        : SslProtocols,
                    CertificateRevocationCheckMode = CertRevocationMode,
                    // Note: RLHttpClient only supports HTTP/1.1 for now, so we only advertise that
                    // to avoid negotiation mismatch with servers that expect H2 frames.
                    ApplicationProtocols = [SslApplicationProtocol.Http11],
                    AllowRenegotiation = false,
                    EncryptionPolicy = EncryptionPolicy.RequireEncryption
                };

                if (CertRevocationMode != X509RevocationMode.Online)
                {
                    sslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
                }

                if (UseCustomCipherSuites && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    sslOptions.CipherSuitesPolicy = new CipherSuitesPolicy(AllowedCipherSuites);
                }

                await sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken).ConfigureAwait(false);
                commonStream = sslStream;
            }
            catch (Exception ex) when (ex is IOException or AuthenticationException)
            {
                tcpClient.Dispose();
                throw new ProxyException("Failed SSL connect");
            }
        }

        return new PooledConnection
        {
            TcpClient = tcpClient,
            NetworkStream = networkStream,
            CommonStream = commonStream,
            Host = host,
            Port = port,
            IsSecure = isSecure,
            LastUsed = DateTime.UtcNow
        };
    }

    private static bool ReturnConnectionToPool(string poolKey, PooledConnection connection)
    {
        if (_connectionPool.TryGetValue(poolKey, out var entry))
        {
            entry.Connections.Enqueue(connection);
            entry.LastAccessed = DateTime.UtcNow;
            return true;
        }

        return false;
    }



    private async Task SendDataAsync(HttpRequest request, Stream stream, CancellationToken cancellationToken = default)
    {
        // Use ArrayBufferWriter to collect the request bytes
        var bufferWriter = new ArrayBufferWriter<byte>();
        await request.WriteToAsync(bufferWriter, cancellationToken).ConfigureAwait(false);

        // Write directly from the buffer writer's committed memory (avoids one full copy)
        var writtenMemory = bufferWriter.WrittenMemory;
        await stream.WriteAsync(writtenMemory, cancellationToken).ConfigureAwait(false);

        // Add to RawRequests with memory limit to prevent leaks
        lock (RawRequests)
        {
            RawRequests.Add(writtenMemory.ToArray());

            // Remove oldest requests if we exceed the limit
            while (RawRequests.Count > MaxRawRequestsToKeep)
            {
                RawRequests.RemoveAt(0);
            }
        }
    }

    private Task<HttpResponse> ReceiveDataAsync(HttpRequest request, Stream stream,
        CancellationToken cancellationToken)
    {
        var pipeReader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        return new HttpResponseBuilder
        {
            ReceiveTimeout = ReceiveTimeout
        }.GetResponseAsync(request, pipeReader, ReadResponseContent, cancellationToken);
    }


    /// <inheritdoc/>
    public void Dispose()
    {
        // Clean up any resources owned by this instance
        // Note: Static connection pool is shared and cleaned up separately
    }

    /// <summary>
    /// Static constructor to initialize cleanup timer for connection pool
    /// </summary>
    static RLHttpClient()
    {
        _poolCleanupTimer = new Timer(CleanupConnectionPool, null,
            TimeSpan.FromMinutes(HttpPerformanceConfig.PoolCleanupIntervalMinutes),
            TimeSpan.FromMinutes(HttpPerformanceConfig.PoolCleanupIntervalMinutes));
    }

    /// <summary>
    /// Clean up all static resources to prevent memory leaks
    /// </summary>
    public static void CleanupStaticResources()
    {
        lock (_connectionPool)
        {
            _poolCleanupTimer?.Dispose();
            _poolCleanupTimer = null;

            foreach (var entry in _connectionPool.Values)
            {
                while (entry.Connections.TryDequeue(out var conn))
                {
                    conn?.Dispose();
                }
            }

            _connectionPool.Clear();
        }
    }
}
