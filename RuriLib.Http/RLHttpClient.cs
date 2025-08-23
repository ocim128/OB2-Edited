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
    private static readonly Timer _poolCleanupTimer = new(CleanupConnectionPool, null, 
        TimeSpan.FromMinutes(HttpPerformanceConfig.PoolCleanupIntervalMinutes), 
        TimeSpan.FromMinutes(HttpPerformanceConfig.PoolCleanupIntervalMinutes));
    
    // Memory optimization with ArrayPool
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    
    // Current connection state
    private TcpClient tcpClient;
    private Stream connectionCommonStream;
    private NetworkStream connectionNetworkStream;
    private string lastConnectedHost;
    private int lastConnectedPort;
    private bool lastConnectionWasSecure;
    private DateTime lastUsed = DateTime.UtcNow;
    
    // Connection pool entry for efficient reuse
    private sealed class ConnectionPoolEntry
    {
        public ConcurrentQueue<PooledConnection> Connections { get; } = new();
        public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
        public int ActiveConnections;
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
            if (entry.LastAccessed < cutoff)
            {
                keysToRemove.Add(kvp.Key);
                continue;
            }
            
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
                    Interlocked.Decrement(ref entry.ActiveConnections);
                }
            }
            
            foreach (var conn in validConnections)
            {
                entry.Connections.Enqueue(conn);
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
        TlsCipherSuite.TLS_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
        TlsCipherSuite.TLS_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA,
        TlsCipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA,
        TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA,
        TlsCipherSuite.TLS_RSA_WITH_3DES_EDE_CBC_SHA
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
            
            if (!response.Headers.TryGetValue("Location", out var location) || string.IsNullOrEmpty(location))
            {
                return response;
            }
            
            var redirectUri = new Uri(currentRequest.Uri, location);
            
            // Change method to GET for non-307 redirects
            var newMethod = response.StatusCode == HttpStatusCode.TemporaryRedirect ? currentRequest.Method : HttpMethod.Get;
            
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
                Headers = redirectHeaders,
                Content = newMethod == HttpMethod.Get ? null : currentRequest.Content
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
        
        try
        {
            await SendDataAsync(request, pooledConnection.CommonStream, cancellationToken).ConfigureAwait(false);
            return await ReceiveDataAsync(request, pooledConnection.CommonStream, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // On error, dispose the connection instead of returning it to pool
            pooledConnection.Dispose();
            throw;
        }
        finally
        {
            // Return connection to pool if still valid
            if (pooledConnection.IsValid)
            {
                ReturnConnectionToPool(poolKey, pooledConnection);
            }
            else
            {
                pooledConnection.Dispose();
            }
        }
    }

    private static string GetPoolKey(string host, int port, bool isSecure)
    {
        return $"{host}:{port}:{isSecure}";
    }

    private async Task<PooledConnection> GetPooledConnectionAsync(string poolKey, string host, int port, bool isSecure, CancellationToken cancellationToken)
    {
        var entry = _connectionPool.GetOrAdd(poolKey, _ => new ConnectionPoolEntry());
        entry.LastAccessed = DateTime.UtcNow;
        
        // Try to get an existing connection
        while (entry.Connections.TryDequeue(out var connection))
        {
            if (connection.IsValid)
            {
                connection.LastUsed = DateTime.UtcNow;
                return connection;
            }
            
            connection.Dispose();
            Interlocked.Decrement(ref entry.ActiveConnections);
        }
        
        // Create new connection if under limit
        if (entry.ActiveConnections < HttpPerformanceConfig.MaxConnectionsPerHost)
        {
            Interlocked.Increment(ref entry.ActiveConnections);
            return await CreateNewConnectionAsync(host, port, isSecure, cancellationToken).ConfigureAwait(false);
        }
        
        // Wait and retry if at limit
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        return await GetPooledConnectionAsync(poolKey, host, port, isSecure, cancellationToken).ConfigureAwait(false);
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
                    EnabledSslProtocols = SslProtocols,
                    CertificateRevocationCheckMode = CertRevocationMode
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

    private static void ReturnConnectionToPool(string poolKey, PooledConnection connection)
    {
        if (_connectionPool.TryGetValue(poolKey, out var entry))
        {
            entry.Connections.Enqueue(connection);
            entry.LastAccessed = DateTime.UtcNow;
        }
        else
        {
            connection.Dispose();
        }
    }



    private async Task SendDataAsync(HttpRequest request, Stream stream, CancellationToken cancellationToken = default)
    {
        // Use ArrayBufferWriter to collect the request bytes
        var bufferWriter = new ArrayBufferWriter<byte>();
        await request.WriteToAsync(bufferWriter, cancellationToken).ConfigureAwait(false);
        var buffer = bufferWriter.WrittenSpan.ToArray();

        await stream.WriteAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);

        RawRequests.Add(buffer);
    }

    private Task<HttpResponse> ReceiveDataAsync(HttpRequest request, Stream stream,
        CancellationToken cancellationToken)
    {
        var pipeReader = PipeReader.Create(stream);
        return new HttpResponseBuilder().GetResponseAsync(request, pipeReader, ReadResponseContent, cancellationToken);
    }

    private async Task CreateConnection(HttpRequest request, CancellationToken cancellationToken)
    {
        var uri = request.Uri;
        var isSecure = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

        // Check if we can reuse the existing connection
        if (CanReuseConnection(uri.Host, uri.Port, isSecure))
        {
            return;
        }

        // Dispose of any previous connection
        await DisposeConnectionAsync().ConfigureAwait(false);

        // Get the stream from the proxies TcpClient
        tcpClient = await ProxyClient.ConnectAsync(uri.Host, uri.Port, null, cancellationToken);
        connectionNetworkStream = tcpClient.GetStream();

        // If https, set up a TLS stream
        if (isSecure)
        {
            try
            {
                var sslStream = new SslStream(connectionNetworkStream, false, ServerCertificateCustomValidationCallback);

                var sslOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = uri.Host,
                    EnabledSslProtocols = SslProtocols,
                    CertificateRevocationCheckMode = CertRevocationMode
                };

                if (CertRevocationMode != X509RevocationMode.Online)
                {
                    sslOptions.RemoteCertificateValidationCallback =
                        static (_, _, _, _) => true;
                }

                if (UseCustomCipherSuites && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    sslOptions.CipherSuitesPolicy = new CipherSuitesPolicy(AllowedCipherSuites);
                }

                connectionCommonStream = sslStream;
                await sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                if (ex is IOException or AuthenticationException)
                {
                    throw new ProxyException("Failed SSL connect");
                }

                throw;
            }
        }
        else
        {
            connectionCommonStream = connectionNetworkStream;
        }

        // Store connection details for reuse
        lastConnectedHost = uri.Host;
        lastConnectedPort = uri.Port;
        lastConnectionWasSecure = isSecure;
    }

    private bool CanReuseConnection(string host, int port, bool isSecure)
    {
        // Check if we have an existing connection
        if (tcpClient == null || connectionCommonStream == null)
            return false;

        // Check if the connection is still alive
        if (!tcpClient.Connected)
            return false;

        // Check if the host, port, and security match
        return string.Equals(lastConnectedHost, host, StringComparison.OrdinalIgnoreCase) &&
               lastConnectedPort == port &&
               lastConnectionWasSecure == isSecure;
    }

    private async Task DisposeConnectionAsync()
    {
        tcpClient?.Close();

        if (connectionCommonStream is not null)
        {
            await connectionCommonStream.DisposeAsync().ConfigureAwait(false);
        }

        if (connectionNetworkStream is not null)
        {
            await connectionNetworkStream.DisposeAsync().ConfigureAwait(false);
        }

        tcpClient = null;
        connectionCommonStream = null;
        connectionNetworkStream = null;
        lastConnectedHost = null;
        lastConnectedPort = 0;
        lastConnectionWasSecure = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        tcpClient?.Dispose();
        connectionCommonStream?.Dispose();
        connectionNetworkStream?.Dispose();
    }
}