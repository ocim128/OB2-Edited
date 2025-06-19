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
using RuriLib.Proxies.Clients;
using RuriLib.Proxies.Exceptions;
using System.Collections.Generic;
using RuriLib.Http.Models;
using System.Linq;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.IO.Pipelines;

namespace RuriLib.Http
{
    /// <summary>
    /// Custom implementation of an HttpClient.
    /// </summary>
    public class RLHttpClient : IDisposable
    {
        private readonly ProxyClient proxyClient;

        private TcpClient tcpClient;
        private Stream connectionCommonStream;
        private NetworkStream connectionNetworkStream;

        private Pipe pipe;
        private PipeWriter writer;

        private readonly ConcurrentDictionary<string, ConcurrentQueue<PooledConnection>> _connectionPool = new();
        private readonly TimeSpan _maxIdleTime = TimeSpan.FromSeconds(60);
        private readonly SemaphoreSlim _throttler;

        // Define the struct here
        private struct PooledConnection
        {
            public TcpClient Client { get; set; }
            public Stream CommonStream { get; set; }
            public DateTime LastUsed { get; set; }
        }

        #region Properties
        /// <summary>
        /// The maximum number of concurrent requests that can be sent at once.
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 50;

        /// <summary>
        /// The underlying proxy client.
        /// </summary>
        public ProxyClient ProxyClient => proxyClient;

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
        /// Gets or sets delegate to verifies the remote Secure Sockets Layer (SSL) 
        /// certificate used for authentication.
        /// </summary>
        public RemoteCertificateValidationCallback ServerCertificateCustomValidationCallback { get; set; }

        /// <summary>
        /// Gets or sets the X509 certificate revocation mode.
        /// </summary>
        public X509RevocationMode CertRevocationMode { get; set; }
        #endregion

        /// <summary>
        /// Creates a new instance of <see cref="RLHttpClient"/> given a <paramref name="proxyClient"/>.
        /// If <paramref name="proxyClient"/> is null, <see cref="NoProxyClient"/> will be used.
        /// </summary>
        public RLHttpClient(ProxyClient proxyClient = null)
        {
            this.proxyClient = proxyClient ?? new NoProxyClient();
            _throttler = new SemaphoreSlim(MaxConcurrentRequests);
        }

        /// <summary>
        /// Asynchronously sends a <paramref name="request"/> and returns an <see cref="HttpResponse"/>.
        /// </summary>
        /// <param name="request">The request to send</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation</param>
        public Task<HttpResponse> SendAsync(HttpRequest request, CancellationToken cancellationToken = default)
            => SendAsync(request, 0, cancellationToken);

        private async Task<HttpResponse> SendAsync(HttpRequest request, int redirects,
            CancellationToken cancellationToken = default)
        {
            await _throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (redirects > MaxNumberOfRedirects)
                {
                    throw new Exception("Maximum number of redirects exceeded");
                }

                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request));
                }

                await CreateConnection(request, cancellationToken).ConfigureAwait(false);
                await SendDataAsync(request, cancellationToken).ConfigureAwait(false);

                var responseMessage = await ReceiveDataAsync(request, cancellationToken).ConfigureAwait(false);

                try
                {
                    // Optionally perform auto redirection on 3xx response
                    if (((int)responseMessage.StatusCode) / 100 == 3 && AllowAutoRedirect)
                    {
                        // Compute the redirection URI
                        var locationHeaderName = responseMessage.Headers.Keys
                            .FirstOrDefault(k => k.Equals("Location", StringComparison.OrdinalIgnoreCase));

                        if (locationHeaderName is null)
                        {
                            throw new Exception($"Status code was {(int)responseMessage.StatusCode} but no Location header received. " +
                                $"Disable auto redirect and try again.");
                        }

                        Uri.TryCreate(responseMessage.Headers[locationHeaderName], UriKind.RelativeOrAbsolute, out var newLocation);

                        var redirectUri = newLocation.IsAbsoluteUri
                            ? newLocation
                            : new Uri(request.Uri, newLocation);

                        // If not 307, change the method to GET
                        if (responseMessage.StatusCode != HttpStatusCode.RedirectKeepVerb)
                        {
                            request.Method = HttpMethod.Get;
                            request.Content = null;
                        }

                        // Adjust the request if the host is different
                        if (request.Uri.Host != redirectUri.Host)
                        {
                            // This is needed otherwise if the Host header was set manually
                            // it will keep the previous one after a domain switch
                            if (request.HeaderExists("Host", out var hostHeaderName))
                            {
                                request.Headers.Remove(hostHeaderName);
                            }

                            // Remove additional headers that could cause trouble
                            request.Headers.Remove("Origin");
                        }

                        // Set the new URI
                        request.Uri = redirectUri;

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
            finally
            {
                _throttler.Release();
            }
        }

        /// <summary>
        /// Asynchronously sends multiple <paramref name="requests"/> in batches and returns a list of <see cref="HttpResponse"/>.
        /// </summary>
        /// <param name="requests">The requests to send</param>
        /// <param name="maxConcurrency">The maximum number of concurrent requests to send</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation</param>
        public async Task<List<HttpResponse>> SendBatchAsync(IEnumerable<HttpRequest> requests, int maxConcurrency = 50, CancellationToken cancellationToken = default)
        {
            using var batchThrottler = new SemaphoreSlim(maxConcurrency);
            var tasks = requests.Select(async request =>
            {
                await batchThrottler.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    batchThrottler.Release();
                }
            });

            return (await Task.WhenAll(tasks)).ToList();
        }

        private async Task SendDataAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            await request.WriteToAsync(writer, cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private Task<HttpResponse> ReceiveDataAsync(HttpRequest request,
            CancellationToken cancellationToken) =>
            new HttpResponseBuilder().GetResponseAsync(request, pipe.Reader, ReadResponseContent, cancellationToken);

        private async Task CreateConnection(HttpRequest request, CancellationToken cancellationToken)
        {
            var uri = request.Uri;
            var key = $"{uri.Host}:{uri.Port}";

            // Try to get a connection from the pool
            if (_connectionPool.TryGetValue(key, out var connections))
            {
                while (connections.TryDequeue(out var entry))
                {
                    if (DateTime.UtcNow - entry.LastUsed < _maxIdleTime && entry.Client.Connected)
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

            // Dispose of any previous connection (if we're coming from a redirect and the previous one was not pooled)
            tcpClient?.Close();

            if (connectionCommonStream is not null)
            {
                await connectionCommonStream.DisposeAsync().ConfigureAwait(false);
            }
            
            if (connectionNetworkStream is not null)
            {
                await connectionNetworkStream.DisposeAsync().ConfigureAwait(false);
            }

            // Get the stream from the proxies TcpClient
            tcpClient = await proxyClient.ConnectAsync(uri.Host, uri.Port, null, cancellationToken);
            connectionNetworkStream = tcpClient.GetStream();

            // Initialize pipe and writer for the new connection
            pipe = new Pipe();
            writer = pipe.Writer;

            // If https, set up a TLS stream
            if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var sslStream = new SslStream(connectionNetworkStream, false, ServerCertificateCustomValidationCallback);
                    await sslStream.AuthenticateAsClientAsync(uri.Host, null, SslProtocols, CertRevocationMode != X509RevocationMode.NoCheck);
                    connectionCommonStream = sslStream;
                }
                catch (Exception)
                {
                    tcpClient.Close();
                    throw;
                }
            }
            else
            {
                connectionCommonStream = connectionNetworkStream;
            }
        }

        private async Task ReturnConnectionToPool(HttpRequest request)
        {
            if (tcpClient == null) return; // No client to return

            var uri = request.Uri;
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

            // Return the connection to the pool
            connections.Enqueue(new PooledConnection
            {
                Client = tcpClient,
                CommonStream = connectionCommonStream,
                LastUsed = DateTime.UtcNow
            });

            // Null out the current client and streams so they aren't disposed when RLHttpClient is disposed
            tcpClient = null;
            connectionCommonStream = null;
            connectionNetworkStream = null;
            pipe = null;
            writer = null;
        }

        /// <summary>
        /// Disposes of the underlying <see cref="TcpClient"/> and <see cref="Stream"/>.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
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