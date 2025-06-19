using RuriLib.Http.Models;
using RuriLib.Models.Bots;
using RuriLib.Proxies; // For ProxyClient
using RuriLib.Proxies.Clients; // For ProxyClient implementations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security; // For TlsCipherSuite
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Functions.Http
{
    public class RLHttpClient : IDisposable
    {
        private readonly HttpClientHandler handler;
        private readonly HttpClient httpClient;
        private bool disposed;

        public List<byte[]> RawRequests { get; } = new List<byte[]>();

        // Properties set by HttpFactory
        public bool AllowAutoRedirect { get; set; }
        public int MaxNumberOfRedirects { get; set; }
        public SslProtocols SslProtocols { get; set; }
        public bool UseCustomCipherSuites { get; set; }
        public TlsCipherSuite[] AllowedCipherSuites { get; set; }
        public X509RevocationMode CertRevocationMode { get; set; }
        public bool ReadResponseContent { get; set; }

        public RLHttpClient(ProxyClient proxyClient)
        {
            // Configure HttpClientHandler with proxy settings
            handler = new HttpClientHandler
            {
                UseProxy = proxyClient != null && proxyClient is not NoProxyClient,
                Proxy = proxyClient != null && proxyClient is not NoProxyClient ? new WebProxy
                {
                    // Construct proxy URI based on proxy type
                    Address = new Uri(proxyClient switch
                    {
                        HttpProxyClient => $"http://{proxyClient.Settings.Host}:{proxyClient.Settings.Port}",
                        Socks4ProxyClient or Socks4aProxyClient => $"socks4://{proxyClient.Settings.Host}:{proxyClient.Settings.Port}",
                        Socks5ProxyClient => $"socks5://{proxyClient.Settings.Host}:{proxyClient.Settings.Port}",
                        NoProxyClient => throw new InvalidOperationException("NoProxyClient does not require a proxy URI"),
                        _ => throw new NotSupportedException($"Unsupported proxy type: {proxyClient.GetType().Name}")
                    }),
                    Credentials = proxyClient.Settings.Credentials
                } : null,
                UseCookies = false // Cookies handled manually in SendAsync
            };

            httpClient = new HttpClient(handler);
        }

        public async Task<HttpResponse> SendAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            // Convert HttpRequest to HttpRequestMessage
            var httpRequestMessage = new HttpRequestMessage
            {
                Method = request.Method,
                RequestUri = request.Uri,
                Version = request.Version
            };

            // Add headers
            foreach (var header in request.Headers)
            {
                httpRequestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Add cookies
            if (request.Cookies != null && request.Cookies.Any())
            {
                var cookieHeader = string.Join("; ", request.Cookies.Select(c => $"{c.Key}={c.Value}"));
                httpRequestMessage.Headers.Add("Cookie", cookieHeader);
            }

            // Add content if present
            if (request.Content != null)
            {
                httpRequestMessage.Content = request.Content;
            }

            // Choose completion option based on ReadResponseContent
            var completionOption = ReadResponseContent
                ? HttpCompletionOption.ResponseContentRead
                : HttpCompletionOption.ResponseHeadersRead;

            // Send request
            HttpResponseMessage responseMessage;
            try
            {
                responseMessage = await httpClient.SendAsync(httpRequestMessage, completionOption, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                httpRequestMessage.Dispose();
                throw;
            }

            // Convert to HttpResponse
            var response = new HttpResponse
            {
                Request = request,
                StatusCode = responseMessage.StatusCode,
                Version = responseMessage.Version,
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            // Copy headers
            foreach (var header in responseMessage.Headers.Concat(responseMessage.Content.Headers))
            {
                response.Headers[header.Key] = string.Join(", ", header.Value);
            }

            // Handle content
            if (ReadResponseContent)
            {
                response.Content = responseMessage.Content;
            }
            else
            {
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                responseMessage.Content?.Dispose(); // Close connection to stop body download
                responseMessage.Dispose();
            }

            // Log raw request
            var requestBytes = await (httpRequestMessage.Content?.ReadAsByteArrayAsync() ?? Task.FromResult(Array.Empty<byte>()));
            RawRequests.Add(requestBytes);

            return response;
        }

        public void Dispose()
        {
            if (!disposed)
            {
                httpClient?.Dispose();
                handler?.Dispose();
                disposed = true;
            }
        }
    }
}
