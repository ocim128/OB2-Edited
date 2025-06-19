using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using RuriLib.Proxies;
using System.Text;
using RuriLib.Proxies.Exceptions;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Buffers;
using System.Collections.Concurrent;
using RuriLib.Proxies.Clients;

namespace RuriLib.Http
{
    /// <summary>
    /// Represents <see cref="HttpMessageHandler"/> with a <see cref="ProxyClient"/>
    /// to provide proxy support to the <see cref="HttpClient"/>.
    /// </summary>
    public class ProxyClientHandler : HttpMessageHandler, IDisposable
    {
        private readonly ProxyClient proxyClient;

        private TcpClient tcpClient;
        private Stream connectionCommonStream;
        private NetworkStream connectionNetworkStream;

        private readonly ConcurrentDictionary<string, ConcurrentQueue<PooledConnection>> _connectionPool = new();
        private readonly TimeSpan _maxIdleTime = TimeSpan.FromSeconds(60);

        // Define the struct here
        private struct PooledConnection
        {
            public TcpClient Client { get; set; }
            public Stream CommonStream { get; set; }
            public DateTime LastUsed { get; set; }
            public DateTime CreationTime { get; set; }
        }

        #region Properties
        /// <summary>
        /// The underlying proxy client.
        /// </summary>
        public ProxyClient ProxyClient => proxyClient;

        /// <summary>
        /// Gets the raw bytes of the last request that was sent.
        /// </summary>
        public List<byte[]> RawRequests { get; } = new();

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
        public bool UseCustomCipherSuites { get; set; } = false;

        /// <summary>
        /// The cipher suites to send to the server during the TLS handshake, in order.
        /// The default value of this property contains the cipher suites sent by Firefox as of 21 Dec 2020.
        /// </summary>
        public TlsCipherSuite[] AllowedCipherSuites { get; set; } = new TlsCipherSuite[]
        {
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
        };

        /// <summary>
        /// Gets the type of decompression method used by the handler for automatic 
        /// decompression of the HTTP content response.
        /// </summary>
        /// <remarks>
        /// Support GZip and Deflate encoding automatically
        /// </remarks>
        public DecompressionMethods AutomaticDecompression => DecompressionMethods.GZip | DecompressionMethods.Deflate;

        /// <summary>
        /// Gets or sets a value that indicates whether the handler uses the CookieContainer
        /// property to store server cookies and uses these cookies when sending requests.
        /// </summary>
        public bool UseCookies { get; set; } = true;

        /// <summary>
        /// Gets or sets the cookie container used to store server cookies by the handler.
        /// </summary>
        public CookieContainer CookieContainer { get; set; }

        /// <summary>
        /// Gets or sets delegate to verifies the remote Secure Sockets Layer (SSL) 
        /// certificate used for authentication.
        /// </summary>
        public RemoteCertificateValidationCallback ServerCertificateCustomValidationCallback { get; set; }

        /// <summary>
        /// Gets or sets the time a pooled connection can be idle before it is disposed.
        /// </summary>
        public TimeSpan ConnectionLeaseTimeout { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Gets or sets the maximum number of connections per server that can be kept in the pool.
        /// </summary>
        public int MaxConnectionsPerServer { get; set; } = 200;

        /// <summary>
        /// Gets or sets the maximum lifetime of a pooled connection before it is disposed, regardless of idle time.
        /// </summary>
        public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the X509 certificate revocation mode.
        /// </summary>
        public X509RevocationMode CertRevocationMode { get; set; }
        #endregion

        /// <summary>
        /// Creates a new instance of <see cref="ProxyClientHandler"/> given a <paramref name="proxyClient"/>.
        /// </summary>
        public ProxyClientHandler(ProxyClient proxyClient)
        {
            this.proxyClient = proxyClient ?? throw new ArgumentNullException(nameof(proxyClient));
        }

        /// <summary>
        /// Asynchronously sends a <paramref name="request"/> and returns an <see cref="HttpResponseMessage"/>.
        /// </summary>
        /// <param name="request">The request to send</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation</param>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken = default)
            => SendAsync(request, 0, cancellationToken);

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            int redirects, CancellationToken cancellationToken = default)
        {
            if (redirects > MaxNumberOfRedirects)
            {
                throw new Exception("Maximum number of redirects exceeded");
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (UseCookies && CookieContainer == null)
            {
                throw new ArgumentNullException(nameof(CookieContainer));
            }

            await CreateConnection(request, cancellationToken).ConfigureAwait(false);
            await SendDataAsync(request, cancellationToken).ConfigureAwait(false);
            
            var responseMessage = await ReceiveDataAsync(request, cancellationToken).ConfigureAwait(false);

            try
            {
                // Optionally perform auto redirection on 3xx response
                if (((int)responseMessage.StatusCode) / 100 == 3 && AllowAutoRedirect)
                {
                    if (!responseMessage.Headers.Contains("Location"))
                    {
                        throw new Exception($"Status code was {(int)responseMessage.StatusCode} but no Location header received. " +
                            $"Disable auto redirect and try again.");
                    }

                    // Compute the redirection URI
                    var redirectUri = responseMessage.Headers.Location.IsAbsoluteUri
                        ? responseMessage.Headers.Location
                        : new Uri(request.RequestUri, responseMessage.Headers.Location);

                    // If not 307, change the method to GET
                    if (responseMessage.StatusCode != HttpStatusCode.RedirectKeepVerb)
                    {
                        request.Method = HttpMethod.Get;
                        request.Content = null;
                    }

                    // Port over the cookies if the domains are different
                    if (request.RequestUri.Host != redirectUri.Host)
                    {
                        var cookies = CookieContainer.GetCookies(request.RequestUri);
                        foreach (Cookie cookie in cookies)
                        {
                            CookieContainer.Add(redirectUri, new Cookie(cookie.Name, cookie.Value));
                        }

                        // This is needed otherwise if the Host header was set manually
                        // it will keep the previous one after a domain switch
                        request.Headers.Host = string.Empty;

                        // Remove additional headers that could cause trouble
                        request.Headers.Remove("Origin");
                    }

                    // Set the new URI
                    request.RequestUri = redirectUri;

                    // Dispose the previous response
                    responseMessage.Dispose();

                    // Perform a new request
                    return await SendAsync(request, redirects + 1, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                responseMessage.Dispose();
                throw;
            }

            return responseMessage;
        }

        private async Task SendDataAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            byte[] buffer;
            using var ms = new MemoryStream();

            // Send the first line
            buffer = Encoding.ASCII.GetBytes(HttpRequestMessageBuilder.BuildFirstLine(request));
            ms.Write(buffer);
            await connectionCommonStream.WriteAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);

            // Send the headers
            buffer = Encoding.ASCII.GetBytes(HttpRequestMessageBuilder.BuildHeaders(request, CookieContainer));
            ms.Write(buffer);
            await connectionCommonStream.WriteAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);

            // Optionally send the content
            if (request.Content != null)
            {
                buffer = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                ms.Write(buffer);
                await connectionCommonStream.WriteAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            }
            RawRequests.Add(ms.ToArray());
        }

        private Pipe pipe;
        private PipeWriter writer;

        private Task<HttpResponseMessage> ReceiveDataAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            new HttpResponseMessageBuilder(1024, CookieContainer, request.RequestUri).GetResponseAsync(request, pipe.Reader, ReadResponseContent, cancellationToken);

        private async Task CreateConnection(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            var key = $"{uri.Host}:{uri.Port}";

            // Try to get a connection from the pool
            if (_connectionPool.TryGetValue(key, out var connections))
            {
                while (connections.TryDequeue(out var entry))
                {
                    if (DateTime.UtcNow - entry.LastUsed < ConnectionLeaseTimeout &&
                        DateTime.UtcNow - entry.CreationTime < PooledConnectionLifetime &&
                        entry.Client.Connected)
                    {
                        tcpClient = entry.Client;
                        connectionCommonStream = entry.CommonStream;
                        connectionNetworkStream = tcpClient.GetStream();

                        // If https, make sure the SslStream is still valid for this host
                        if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                        {
                            if (connectionCommonStream is SslStream sslStream && sslStream.IsAuthenticated)
                            {
                                // Initialize pipe and writer for the reused connection
                                pipe = new Pipe();
                                writer = pipe.Writer;
                                return;
                            }
                        }
                        else // For http connections, just return
                        {
                            pipe = new Pipe();
                            writer = pipe.Writer;
                            return;
                        }
                    }
                    // If connection is not valid, dispose of it
                    entry.Client.Dispose();
                    entry.CommonStream.Dispose();
                }
            }

            // If no connection in pool or none valid, create a new one
            if (tcpClient == null)
            {
                // Check if we exceed MaxConnectionsPerServer
                if (_connectionPool.TryGetValue(key, out var existingConnections) && existingConnections.Count >= MaxConnectionsPerServer)
                {
                    // If we have too many connections in the pool, try to reuse one even if it's stale
                    // or just dispose and retry, for now, let's just dispose of one and retry
                    if (existingConnections.TryDequeue(out var entry))
                    {
                        entry.Client.Dispose();
                        entry.CommonStream.Dispose();
                    }
                }

                tcpClient = await proxyClient.ConnectAsync(uri.Host, uri.Port, null, cancellationToken).ConfigureAwait(false);

                // Get the network stream and potentially wrap it in an SSL stream
                connectionNetworkStream = tcpClient.GetStream();
                connectionCommonStream = connectionNetworkStream;

                if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                {
                    var sslStream = new SslStream(connectionNetworkStream,
                        false,
                        ServerCertificateCustomValidationCallback,
                        null,
                        EncryptionPolicy.RequireEncryption);

                    var options = new SslClientAuthenticationOptions
                    {
                        TargetHost = uri.Host,
                        ClientCertificates = null,
                        EnabledSslProtocols = SslProtocols,
                        CertificateRevocationCheckMode = CertRevocationMode,
                        // Commented out since CipherSuitesPolicy is unsupported on Windows
                        // CipherSuitesPolicy = UseCustomCipherSuites
                        //     ? new CipherSuitesPolicy(AllowedCipherSuites)
                        //     : null
                    };

                    if (UseCustomCipherSuites)
                    { // Manually set if supported
                        try
                        {
                            var policy = new CipherSuitesPolicy(AllowedCipherSuites);
                            // This line will only compile and run on platforms that support CipherSuitesPolicy in SslClientAuthenticationOptions
                            // options.CipherSuitesPolicy = policy;
                        }
                        catch (PlatformNotSupportedException)
                        {
                            // Log or handle the case where CipherSuitesPolicy is not supported (e.g., on Windows)
                            Console.WriteLine("CipherSuitesPolicy is not supported on this platform. Custom cipher suites will not be used.");
                        }
                    }

                    try
                    {
                        await sslStream.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Dispose the client if authentication fails
                        tcpClient.Dispose();
                        connectionCommonStream.Dispose();
                        connectionNetworkStream.Dispose();
                        throw new ProxyException("Could not authenticate SSL stream: " + ex.Message, ex);
                    }
                    connectionCommonStream = sslStream;
                }

                pipe = new Pipe();
                writer = pipe.Writer;
            }
        }

        private async Task ReturnConnectionToPool(HttpRequestMessage request)
        {
            if (tcpClient == null) return; // No client to return

            var uri = request.RequestUri;
            var key = $"{uri.Host}:{uri.Port}";

            if (!_connectionPool.TryGetValue(key, out var connections))
            {
                connections = new ConcurrentQueue<PooledConnection>();
                _connectionPool.TryAdd(key, connections);
            }

            // Before returning, ensure the writer is completed if it exists
            if (writer != null)
            {
                await writer.CompleteAsync();
            }

            // Only return if pool size is less than MaxConnectionsPerServer
            if (connections.Count < MaxConnectionsPerServer)
            {
                // Return the connection to the pool
                connections.Enqueue(new PooledConnection
                {
                    Client = tcpClient,
                    CommonStream = connectionCommonStream,
                    LastUsed = DateTime.UtcNow,
                    CreationTime = DateTime.UtcNow // Set creation time
                });

                // Null out the current client and streams so they aren't disposed when ProxyClientHandler is disposed
                tcpClient = null;
                connectionCommonStream = null;
                connectionNetworkStream = null;
                pipe = null;
                writer = null;
            }
            else
            {
                // Dispose the connection if the pool is full
                tcpClient.Dispose();
                connectionCommonStream.Dispose();
                connectionNetworkStream.Dispose();
                tcpClient = null;
                connectionCommonStream = null;
                connectionNetworkStream = null;
                pipe = null;
                writer = null;
            }
        }

        /// <summary>
        /// Disposes of the underlying <see cref="TcpClient"/> and <see cref="Stream"/>.
        /// </summary>
        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose of current connection if it hasn't been returned to the pool
                tcpClient?.Dispose();
                connectionCommonStream?.Dispose();
                connectionNetworkStream?.Dispose();

                // Dispose of all pooled connections
                foreach (var queue in _connectionPool.Values)
                {
                    while (queue.TryDequeue(out var entry))
                    {
                        entry.Client.Dispose();
                        entry.CommonStream.Dispose();
                    }
                }
            }
        }
    }
}