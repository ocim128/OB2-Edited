using RuriLib.Functions.Http.Options;
using RuriLib.Http.Models;
using RuriLib.Models.Bots;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Functions.Http
{
    /// <summary>
    /// High-performance HTTP request handler using optimized <see cref="RLHttpClient"/> with advanced connection pooling.
    /// </summary>
    internal class RLHttpClientRequestHandler : HttpRequestHandler
    {
        private static readonly ConcurrentDictionary<string, ClientPoolEntry> clientPool = new();
        private static readonly Timer cleanupTimer = new(CleanupExpiredClients, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
        private const int MaxClientsPerKey = 8;
        private const int ClientTimeoutMinutes = 3;

        private sealed class ClientPoolEntry
        {
            public ConcurrentQueue<PooledClient> Clients { get; } = new();
            public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
            public int ActiveClients;
        }

        private sealed class PooledClient : IDisposable
        {
            public required RuriLib.Http.RLHttpClient Client { get; init; }
            public required string Key { get; init; }
            public DateTime LastUsed { get; set; } = DateTime.UtcNow;

            public bool IsValid => (DateTime.UtcNow - LastUsed).TotalMinutes < ClientTimeoutMinutes;

            public void Dispose()
            {
                Client.Dispose();
            }
        }

        public override Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
            => options.UseTlsFingerprinting
                ? new TlsClientRequestHandler().HttpRequestStandard(data, options)
                : ExecutePipelineAsync(
                    data,
                    CreateNormalizedRequest(data, options),
                    "RuriLibHttp",
                    (request, token) => SendAsync(data, options, request, token));

        public override Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
            => options.UseTlsFingerprinting
                ? new TlsClientRequestHandler().HttpRequestRaw(data, options)
                : ExecutePipelineAsync(
                    data,
                    CreateNormalizedRequest(data, options),
                    "RuriLibHttp",
                    (request, token) => SendAsync(data, options, request, token));

        public override Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
            => options.UseTlsFingerprinting
                ? new TlsClientRequestHandler().HttpRequestBasicAuth(data, options)
                : ExecutePipelineAsync(
                    data,
                    CreateNormalizedRequest(data, options),
                    "RuriLibHttp",
                    (request, token) => SendAsync(data, options, request, token));

        public override Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options)
            => options.UseTlsFingerprinting
                ? new TlsClientRequestHandler().HttpRequestMultipart(data, options)
                : ExecutePipelineAsync(
                    data,
                    CreateNormalizedRequest(data, options),
                    "RuriLibHttp",
                    (request, token) => SendAsync(data, options, request, token));

        private static async Task<NormalizedHttpResponse> SendAsync(
            BotData data,
            RuriLib.Functions.Http.Options.HttpRequestOptions options,
            NormalizedHttpRequest request,
            CancellationToken cancellationToken)
        {
            var clientOptions = GetClientOptions(data, options);
            clientOptions.AutoRedirect = false;
            clientOptions.MaxNumberOfRedirects = 0;

            var pooledClient = GetOrCreateClient(data, clientOptions);
            FileStream? fileStream = null;

            try
            {
                using var content = CreateHttpContent(data, request, out fileStream);
                using var rlRequest = new HttpRequest
                {
                    Method = request.Method,
                    Uri = request.Uri,
                    Version = request.Version,
                    Headers = CopyHeaders(request),
                    Cookies = CopyCookies(request),
                    AbsoluteUriInFirstLine = request.AbsoluteUriInFirstLine,
                    Content = content
                };

                using var response = await pooledClient.Client.SendAsync(rlRequest, cancellationToken).ConfigureAwait(false);
                var statusCode = (int)response.StatusCode;
                var body = request.ReadResponseContent
                    ? await ReadResponseBodyAsync(response, cancellationToken).ConfigureAwait(false)
                    : Array.Empty<byte>();
                var headers = NormalizeHeaders(response.Headers);

                if (response.Content?.Headers != null)
                {
                    foreach (var header in response.Content.Headers)
                    {
                        if (!headers.TryGetValue(header.Key, out var values))
                        {
                            values = new List<string>();
                            headers[header.Key] = values;
                        }

                        values.AddRange(header.Value);
                    }
                }

                var address = response.Request.Uri.IsAbsoluteUri
                    ? response.Request.Uri
                    : new Uri(request.Uri, response.Request.Uri);

                return CreateResponseSnapshot(address, statusCode, headers, body);
            }
            finally
            {
                if (fileStream != null)
                {
                    await fileStream.DisposeAsync().ConfigureAwait(false);
                }

                ReturnClient(pooledClient);
            }
        }

        private static async Task<byte[]> ReadResponseBodyAsync(HttpResponse response, CancellationToken cancellationToken)
        {
            try
            {
                return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (NullReferenceException)
            {
                return Array.Empty<byte>();
            }
        }

        private static PooledClient GetOrCreateClient(BotData data, HttpOptions clientOptions)
        {
            var key = GenerateClientKey(data, clientOptions);
            var poolEntry = clientPool.GetOrAdd(key, _ => new ClientPoolEntry());
            poolEntry.LastAccessed = DateTime.UtcNow;

            while (poolEntry.Clients.TryDequeue(out var pooledClient))
            {
                if (pooledClient.IsValid)
                {
                    pooledClient.LastUsed = DateTime.UtcNow;
                    return pooledClient;
                }

                pooledClient.Dispose();
                Interlocked.Decrement(ref poolEntry.ActiveClients);
            }

            if (poolEntry.ActiveClients < MaxClientsPerKey)
            {
                Interlocked.Increment(ref poolEntry.ActiveClients);
                return new PooledClient
                {
                    Client = HttpFactory.GetRLHttpClient(data.UseProxy ? data.Proxy : null, clientOptions),
                    Key = key
                };
            }

            return new PooledClient
            {
                Client = HttpFactory.GetRLHttpClient(data.UseProxy ? data.Proxy : null, clientOptions),
                Key = key
            };
        }

        private static void ReturnClient(PooledClient pooledClient)
        {
            if (!pooledClient.IsValid)
            {
                pooledClient.Dispose();

                if (clientPool.TryGetValue(pooledClient.Key, out var poolEntry))
                {
                    Interlocked.Decrement(ref poolEntry.ActiveClients);
                }
                return;
            }

            if (clientPool.TryGetValue(pooledClient.Key, out var entry))
            {
                entry.Clients.Enqueue(pooledClient);
            }
            else
            {
                pooledClient.Dispose();
            }
        }

        private static string GenerateClientKey(BotData data, HttpOptions clientOptions)
        {
            var proxy = data.UseProxy ? data.Proxy : null;
            string proxyKey;
            if (proxy != null)
            {
                var creds = proxy.NeedsAuthentication
                    ? $":{proxy.Username}:{proxy.Password}"
                    : "";
                proxyKey = $"{proxy.Type}:{proxy.Host}:{proxy.Port}{creds}";
            }
            else
            {
                proxyKey = "noproxy";
            }

            var cipherKey = clientOptions.UseCustomCipherSuites && clientOptions.CustomCipherSuites is { Length: > 0 }
                ? string.Join(",", clientOptions.CustomCipherSuites)
                : "default";

            return $"{proxyKey}:{clientOptions.SecurityProtocol}:{cipherKey}:{clientOptions.CertRevocationMode}:{clientOptions.ConnectTimeout.Ticks}:{clientOptions.ReadWriteTimeout.Ticks}";
        }

        private static void CleanupExpiredClients(object? state)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-ClientTimeoutMinutes);
            var keysToRemove = new List<string>();

            foreach (var kvp in clientPool)
            {
                var poolEntry = kvp.Value;
                var retainedClients = new List<PooledClient>();
                var disposedClients = 0;

                while (poolEntry.Clients.TryDequeue(out var client))
                {
                    if (client.IsValid)
                    {
                        retainedClients.Add(client);
                    }
                    else
                    {
                        client.Dispose();
                        disposedClients++;
                    }
                }

                if (disposedClients > 0)
                {
                    Interlocked.Add(ref poolEntry.ActiveClients, -disposedClients);
                }

                foreach (var client in retainedClients)
                {
                    poolEntry.Clients.Enqueue(client);
                }

                if (poolEntry.LastAccessed < cutoff && poolEntry.ActiveClients == 0)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (clientPool.TryRemove(key, out var poolEntry))
                {
                    while (poolEntry.Clients.TryDequeue(out var client))
                    {
                        client.Dispose();
                    }
                }
            }
        }
    }
}
