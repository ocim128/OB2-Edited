using RuriLib.Extensions;
using RuriLib.Functions.Files;
using RuriLib.Functions.Http.Options;
using RuriLib.Helpers;
using RuriLib.Http.Models;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Buffers;
using RuriLib.Functions.Conversion;

namespace RuriLib.Functions.Http
{
    /// <summary>
    /// High-performance HTTP request handler using optimized <see cref="RLHttpClient"/> with advanced connection pooling.
    /// </summary>
    internal class RLHttpClientRequestHandler : HttpRequestHandler
    {
        private static readonly ConcurrentDictionary<string, ClientPoolEntry> _clientPool = new();
        private static readonly Timer _cleanupTimer = new(CleanupExpiredClients, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
        private const int MaxClientsPerKey = 8;
        private const int ClientTimeoutMinutes = 3;

        // Fast-path check for common network exceptions to avoid full recursive check
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLikelyNetworkException(Exception ex)
        {
            if (ex == null)
                return false;

            // Fast path for most common network exceptions (single type check)
            // Use type comparison for better performance in hot paths
            var exType = ex.GetType();
            if (exType == typeof(HttpRequestException) ||
                exType == typeof(WebException) ||
                exType == typeof(SocketException) ||
                exType == typeof(TimeoutException))
                return true;

            // Only do the full check for less common exceptions
            if (exType == typeof(OperationCanceledException) || exType == typeof(IOException))
                return NetworkExceptionHelper.IsNetworkException(ex);

            return false;
        }

        // Memory optimization pools
        private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
        private static readonly ConcurrentQueue<Dictionary<string, string>> _headerDictionaryPool = new();

        private sealed class ClientPoolEntry
        {
            public ConcurrentQueue<PooledClient> Clients { get; } = new();
            public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
            public int ActiveClients;
        }

        private sealed class PooledClient : IDisposable
        {
            public RLHttpClient Client { get; set; }
            public DateTime LastUsed { get; set; } = DateTime.UtcNow;
            public string Key { get; set; }

            public bool IsValid => (DateTime.UtcNow - LastUsed).TotalMinutes < ClientTimeoutMinutes;

            public void Dispose()
            {
                Client?.Dispose();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Dictionary<string, string> GetPooledHeaderDictionary()
        {
            if (_headerDictionaryPool.TryDequeue(out var dict))
            {
                dict.Clear();
                return dict;
            }
            return new Dictionary<string, string>(16, StringComparer.OrdinalIgnoreCase);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReturnHeaderDictionary(Dictionary<string, string> dict)
        {
            if (dict.Count <= 32)
            {
                _headerDictionaryPool.Enqueue(dict);
            }
        }

        private static PooledClient GetOrCreateClient(BotData data, HttpOptions clientOptions)
        {
            var key = GenerateClientKey(data, clientOptions);

            var poolEntry = _clientPool.GetOrAdd(key, _ => new ClientPoolEntry());
            poolEntry.LastAccessed = DateTime.UtcNow;

            // Try to get an existing valid client
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

            // Create new client if under limit
            if (poolEntry.ActiveClients < MaxClientsPerKey)
            {
                var newClient = HttpFactory.GetRLHttpClient(data.UseProxy ? data.Proxy : null, clientOptions);
                var newPooledClient = new PooledClient
                {
                    Client = newClient,
                    Key = key,
                    LastUsed = DateTime.UtcNow
                };
                Interlocked.Increment(ref poolEntry.ActiveClients);
                return newPooledClient;
            }

            // Fallback: create temporary client (not pooled)
            return new PooledClient
            {
                Client = HttpFactory.GetRLHttpClient(data.UseProxy ? data.Proxy : null, clientOptions),
                Key = key,
                LastUsed = DateTime.UtcNow
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReturnClient(PooledClient pooledClient)
        {
            if (pooledClient?.Client == null || !pooledClient.IsValid)
            {
                pooledClient?.Dispose();
                return;
            }

            if (_clientPool.TryGetValue(pooledClient.Key, out var poolEntry))
            {
                poolEntry.Clients.Enqueue(pooledClient);
            }
            else
            {
                pooledClient.Dispose();
            }
        }

        private static string GenerateClientKey(BotData data, HttpOptions clientOptions)
        {
            var proxy = data.UseProxy ? data.Proxy : null;
            var proxyKey = proxy != null ? $"{proxy.Type}:{proxy.Host}:{proxy.Port}" : "noproxy";
            return $"{proxyKey}:{clientOptions.SecurityProtocol}:{clientOptions.UseCustomCipherSuites}";
        }

        private static void CleanupExpiredClients(object state)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-ClientTimeoutMinutes);
            var keysToRemove = new List<string>();
            var validClientsBuffer = new List<PooledClient>(MaxClientsPerKey);

            foreach (var kvp in _clientPool)
            {
                var poolEntry = kvp.Value;
                validClientsBuffer.Clear();

                // Clean expired clients from the pool - batch process for efficiency
                int expiredCount = 0;
                while (poolEntry.Clients.TryDequeue(out var client))
                {
                    if (client.IsValid)
                    {
                        validClientsBuffer.Add(client);
                    }
                    else
                    {
                        client.Dispose();
                        expiredCount++;
                    }
                }

                // Update active client count in batch
                if (expiredCount > 0)
                {
                    Interlocked.Add(ref poolEntry.ActiveClients, -expiredCount);
                }

                // Re-enqueue valid clients in batch
                for (int i = 0; i < validClientsBuffer.Count; i++)
                {
                    poolEntry.Clients.Enqueue(validClientsBuffer[i]);
                }

                // Mark pool entry for removal if unused and empty
                if (poolEntry.LastAccessed < cutoff && poolEntry.ActiveClients == 0)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            // Remove expired pool entries in batch
            foreach (var key in keysToRemove)
            {
                if (_clientPool.TryRemove(key, out var poolEntry))
                {
                    // Dispose any remaining clients
                    while (poolEntry.Clients.TryDequeue(out var client))
                    {
                        client.Dispose();
                    }
                }
            }
        }
        public async override Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
        {
            var clientOptions = GetClientOptions(data, options);

            var pooledClient = GetOrCreateClient(data, clientOptions);
            var client = pooledClient.Client;

            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            using var request = new HttpRequest
            {
                Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                Uri = new Uri(options.Url),
                Version = Version.Parse(options.HttpVersion),
                Headers = options.CustomHeaders,
                Cookies = data.COOKIES,
                AbsoluteUriInFirstLine = options.AbsoluteUriInFirstLine
            };

            if (!string.IsNullOrEmpty(options.Content) || options.AlwaysSendContent)
            {
                var content = options.Content;

                if (options.UrlEncodeContent)
                {
                    content = string.Join("", content.SplitInChunks(2080)
                        .Select(Uri.EscapeDataString))
                        .Replace($"%26", "&").Replace($"%3D", "=");
                }

                request.Content = new StringContent(content.Unescape());
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(options.ContentType);
            }

            data.Logger.LogHeader();

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);
                using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

                await LogHttpRequestData(data, request).ConfigureAwait(false);

                // Add generic redirect handling for all 3xx codes
                if (options.AutoRedirect && options.MaxNumberOfRedirects > 0 &&
                    (int)response.StatusCode >= 300 && (int)response.StatusCode < 400 &&
                    response.Headers.ContainsKey("Location"))
                {
                    // Log initial response to capture headers and cookies
                    await LogHttpResponseData(data, response, request, options).ConfigureAwait(false);
                    var locationValue = response.Headers["Location"];
                    var newUri = Uri.TryCreate(locationValue, UriKind.Absolute, out var absUri)
                        ? absUri
                        : new Uri(request.Uri, locationValue);
                    // Forward all cookies for the next request
                    var redirectOptions = new StandardHttpRequestOptions
                    {
                        Url = newUri.ToString(),
                        Method = HttpMethod.GET,
                        AutoRedirect = true,
                        MaxNumberOfRedirects = options.MaxNumberOfRedirects - 1,
                        HttpLibrary = HttpLibrary.SystemNet,
                        CustomCookies = new Dictionary<string, string>(data.COOKIES),
                        CustomHeaders = new Dictionary<string, string>(),
                        TimeoutMilliseconds = options.TimeoutMilliseconds,
                        ReadResponseContent = options.ReadResponseContent,
                        DecodeHtml = options.DecodeHtml,
                        DisableCookieParsing = options.DisableCookieParsing,
                        DisableHeaderParsing = options.DisableHeaderParsing,
                        AbsoluteUriInFirstLine = options.AbsoluteUriInFirstLine
                    };
                    if (options.CustomHeaders.TryGetValue("User-Agent", out var ua))
                        redirectOptions.CustomHeaders["User-Agent"] = ua;
                    response.Dispose();
                    await HttpRequestStandard(data, redirectOptions).ConfigureAwait(false);
                    return;
                }

                await LogHttpResponseData(data, response, request, options).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsLikelyNetworkException(ex))
            {
                await LogHttpRequestData(data, request).ConfigureAwait(false);
                data.Logger.Log($"Network exception detected: {ex.GetType().Name} - {ex.Message}", LogColors.Orange);
                throw; // Re-throw to be caught by the retry logic
            }
            finally
            {
                ReturnClient(pooledClient);
            }
        }

        public async override Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
        {
            var clientOptions = GetClientOptions(data, options);
            var pooledClient = GetOrCreateClient(data, clientOptions);
            var client = pooledClient.Client;

            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            using var request = new HttpRequest
            {
                Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                Uri = new Uri(options.Url),
                Version = Version.Parse(options.HttpVersion),
                Headers = options.CustomHeaders,
                Cookies = data.COOKIES,
                AbsoluteUriInFirstLine = options.AbsoluteUriInFirstLine,
                Content = new ByteArrayContent(options.Content)
            };

            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(options.ContentType);

            data.Logger.LogHeader();

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);
                using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

                await LogHttpRequestData(data, request).ConfigureAwait(false);
                await LogHttpResponseData(data, response, request, options).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsLikelyNetworkException(ex))
            {
                await LogHttpRequestData(data, request).ConfigureAwait(false);
                data.Logger.Log($"Network exception detected: {ex.GetType().Name} - {ex.Message}", LogColors.Orange);
                throw; // Re-throw to be caught by the retry logic
            }
            finally
            {
                ReturnClient(pooledClient);
            }
        }

        public async override Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
        {
            var clientOptions = GetClientOptions(data, options);
            var pooledClient = GetOrCreateClient(data, clientOptions);
            var client = pooledClient.Client;

            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            using var request = new HttpRequest
            {
                Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                Uri = new Uri(options.Url),
                Version = Version.Parse(options.HttpVersion),
                Headers = options.CustomHeaders,
                Cookies = data.COOKIES,
                AbsoluteUriInFirstLine = options.AbsoluteUriInFirstLine
            };

            // Add the basic auth header
            request.AddHeader("Authorization", "Basic " + Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}")));

            data.Logger.LogHeader();

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);
                using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

                await LogHttpRequestData(data, request).ConfigureAwait(false);
                await LogHttpResponseData(data, response, request, options).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsLikelyNetworkException(ex))
            {
                await LogHttpRequestData(data, request).ConfigureAwait(false);
                data.Logger.Log($"Network exception detected: {ex.GetType().Name} - {ex.Message}", LogColors.Orange);
                throw; // Re-throw to be caught by the retry logic
            }
            finally
            {
                ReturnClient(pooledClient);
            }
        }

        public async override Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options)
        {
            var clientOptions = GetClientOptions(data, options);
            var pooledClient = GetOrCreateClient(data, clientOptions);
            var client = pooledClient.Client;

            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            if (string.IsNullOrWhiteSpace(options.Boundary))
                options.Boundary = GenerateMultipartBoundary();

            // Rewrite the value of the Content-Type header otherwise it will add double quotes around it like
            // Content-Type: multipart/form-data; boundary="------WebKitFormBoundaryewozmkbxwbblilpm"
            var multipartContent = new MultipartFormDataContent(options.Boundary);
            multipartContent.Headers.ContentType.Parameters.First(o => o.Name == "boundary").Value = options.Boundary;

            FileStream fileStream = null;

            foreach (var c in options.Contents)
            {
                switch (c)
                {
                    case StringHttpContent x:
                        multipartContent.Add(new StringContent(x.Data, Encoding.UTF8, x.ContentType), x.Name);
                        break;

                    case RawHttpContent x:
                        var byteContent = new ByteArrayContent(x.Data);
                        byteContent.Headers.ContentType = new MediaTypeHeaderValue(x.ContentType);
                        multipartContent.Add(byteContent, x.Name);
                        break;

                    case FileHttpContent x:
                        lock (FileLocker.GetHandle(x.FileName))
                        {
                            if (data.Providers.Security.RestrictBlocksToCWD)
                                FileUtils.ThrowIfNotInCWD(x.FileName);

                            fileStream = new FileStream(x.FileName, FileMode.Open);
                            var fileContent = CreateFileContent(fileStream, x.Name, Path.GetFileName(x.FileName), x.ContentType);
                            multipartContent.Add(fileContent, x.Name);
                        }
                        break;
                }
            }

            using var request = new HttpRequest
            {
                Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                Uri = new Uri(options.Url),
                Version = Version.Parse(options.HttpVersion),
                Headers = options.CustomHeaders,
                Cookies = data.COOKIES,
                AbsoluteUriInFirstLine = options.AbsoluteUriInFirstLine,
                Content = multipartContent
            };

            data.Logger.LogHeader();

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);
                using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

                await LogHttpRequestData(data, request, options.Boundary, options.Contents).ConfigureAwait(false);
                await LogHttpResponseData(data, response, request, options).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsLikelyNetworkException(ex))
            {
                await LogHttpRequestData(data, request, options.Boundary, options.Contents).ConfigureAwait(false);
                data.Logger.Log($"Network exception detected: {ex.GetType().Name} - {ex.Message}", LogColors.Orange);
                throw; // Re-throw to be caught by the retry logic
            }
            finally
            {
                if (fileStream != null)
                    await fileStream.DisposeAsync().ConfigureAwait(false);
                ReturnClient(pooledClient);
            }
        }

        private static async Task LogHttpRequestData(BotData data, HttpRequest request,
            string boundary = null, List<MyHttpContent> multipartContents = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{request.Method.Method} {request.Uri.PathAndQuery} HTTP/{request.Version.Major}.{request.Version.Minor}");

            // Log the headers
            if (!request.HeaderExists("Host", out _))
                sb.AppendLine($"Host: {request.Uri.Host}");

            foreach (var header in request.Headers)
            {
                sb.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
            }

            // Log the cookie header (only if not already in headers to avoid duplication)
            if (!request.HeaderExists("Cookie", out _))
            {
                var cookies = request.Cookies.Select(c => $"{c.Key}={c.Value}");

                if (cookies.Any())
                    sb.AppendLine($"Cookie: {string.Join("; ", cookies)}");
            }

            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    sb.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
                }

                if (request.Content is StringContent stringContent)
                {
                    sb.AppendLine();
                    var content = await stringContent.ReadAsStringAsync().ConfigureAwait(false);
                    sb.AppendLine(content);
                }
                else if (request.Content is ByteArrayContent byteArrayContent)
                {
                    sb.AppendLine();
                    var bytes = await byteArrayContent.ReadAsByteArrayAsync().ConfigureAwait(false);
                    sb.AppendLine(Base64Converter.ToBase64String(bytes));
                }
                else if (request.Content is MultipartFormDataContent)
                {
                    sb.AppendLine();
                    sb.AppendLine(SerializeMultipart(boundary, multipartContents));
                }
            }

            data.Logger.Log(sb.ToString(), LogColors.Azure);
        }

        private static async Task LogHttpResponseData(BotData data, HttpResponse response, HttpRequest request,
            RuriLib.Functions.Http.Options.HttpRequestOptions requestOptions)
        {
            // Skip reading payload on redirects and read content only if requested
            int status = (int)response.StatusCode;
            if (status >= 300 && status < 400)
            {
                data.RAWSOURCE = Array.Empty<byte>();
            }
            else if (requestOptions.ReadResponseContent)
            {
                try
                {
                    data.RAWSOURCE = await response.Content.ReadAsByteArrayAsync(data.CancellationToken).ConfigureAwait(false);
                }
                catch (NullReferenceException)
                {
                    data.RAWSOURCE = Array.Empty<byte>();
                }
            }
            else
            {
                data.RAWSOURCE = Array.Empty<byte>();
            }

            // Address
            var uri = response.Request.Uri;
            if (!uri.IsAbsoluteUri)
                uri = new Uri(request.Uri, uri);
            data.ADDRESS = response.Request.Uri.AbsoluteUri;
            data.Logger.Log($"Address: {data.ADDRESS}", LogColors.DodgerBlue);

            // Response code
            data.RESPONSECODE = (int)response.StatusCode;
            data.Logger.Log($"Response code: {data.RESPONSECODE}", LogColors.Citrine);

            // Headers (conditional parsing for speed)
            if (!requestOptions.DisableHeaderParsing)
            {
                static string GetHeaderValue(KeyValuePair<string, string> header)
                {
                    // For Set-Cookie headers, show only the name=value part
                    if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
                        header.Key.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase))
                    {
                        var cookieList = new List<string>();

                        // Split the header by semicolons and commas to find individual cookies
                        var cookieHeaders = SplitSetCookieHeaders(header.Value);

                        foreach (var cookieHeader in cookieHeaders)
                        {
                            if (TryParseCookieForDisplay(cookieHeader.Trim(), out var cookieName, out var cookieValue))
                            {
                                cookieList.Add($"{cookieName}={cookieValue}");
                            }
                        }

                        if (cookieList.Count > 0)
                        {
                            return string.Join("; ", cookieList);
                        }
                        else
                        {
                            // Fallback to original value if no valid cookies found
                            return header.Value;
                        }
                    }
                    else
                    {
                        return header.Value;
                    }
                }

                var sbHeaders = new StringBuilder();
                sbHeaders.AppendLine("Received Headers:");
                data.HEADERS = response.Headers;
                if (response.Content != null)
                {
                    foreach (var header in response.Content.Headers)
                    {
                        data.HEADERS[header.Key] = string.Join(", ", header.Value);
                        sbHeaders.AppendLine($"{header.Key}: {GetHeaderValue(new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value)))}");
                    }
                }

                foreach (var header in response.Headers)
                {
                    sbHeaders.AppendLine($"{header.Key}: {GetHeaderValue(header)}");
                }

                if (!data.HEADERS.ContainsKey("Content-Length"))
                    data.HEADERS["Content-Length"] = data.RAWSOURCE.Length.ToString();

                data.Logger.Log(sbHeaders.ToString(), LogColors.Violet);
            }
            else
            {
                data.HEADERS.Clear();
                data.HEADERS["Content-Length"] = data.RAWSOURCE.Length.ToString();
                data.Logger.Log("Header Parsing Skipped", LogColors.Orange);
            }

            // Cookies (conditional parsing for speed)
            // Always merge cookies already stored by HttpResponseBuilder (these come from Set-Cookie headers)
            foreach (var kv in response.Request.Cookies)
            {
                data.COOKIES[kv.Key] = kv.Value;
            }

            if (!requestOptions.DisableCookieParsing)
            {
                // Parse Set-Cookie headers from response
                // Handle multiple Set-Cookie headers (Instagram sends multiple)
                if (response.Headers.ContainsKey("Set-Cookie"))
                {
                    var setCookieHeader = response.Headers["Set-Cookie"];
                    // Multiple Set-Cookie headers are typically joined with commas, but we need to split carefully
                    // because cookies can contain commas in expires dates
                    var cookieHeaders = SplitSetCookieHeaders(setCookieHeader);
                    foreach (var cookieHeader in cookieHeaders)
                    {
                        if (TryParseCookie(cookieHeader.Trim(), out var cookieName, out var cookieValue))
                        {
                            data.COOKIES[cookieName] = cookieValue;
                        }
                    }
                }

                // Also check for Set-Cookie2 (handle multiple values)
                if (response.Headers.ContainsKey("Set-Cookie2"))
                {
                    var setCookieHeader = response.Headers["Set-Cookie2"];
                    var cookieHeaders = SplitSetCookieHeaders(setCookieHeader);
                    foreach (var cookieHeader in cookieHeaders)
                    {
                        if (TryParseCookie(cookieHeader.Trim(), out var cookieName, out var cookieValue))
                        {
                            data.COOKIES[cookieName] = cookieValue;
                        }
                    }
                }

                var sbCookies = new StringBuilder();
                sbCookies.AppendLine("Received Cookies:");
                foreach (var cookie in data.COOKIES)
                {
                    sbCookies.AppendLine($"{cookie.Key}: {cookie.Value}");
                }
                data.Logger.Log(sbCookies.ToString(), LogColors.Khaki);
            }
            else
            {
                // Don't clear existing cookies, just skip parsing new ones
                data.Logger.Log("Cookie Parsing Skipped", LogColors.Orange);
            }

            static bool TryParseCookie(string cookieHeader, out string cookieName, out string cookieValue)
            {
                cookieName = null;
                cookieValue = null;

                if (string.IsNullOrEmpty(cookieHeader))
                {
                    return false;
                }

                var separatorPos = cookieHeader.IndexOf('=');
                if (separatorPos <= 0)
                {
                    // Invalid cookie, don't add it
                    return false;
                }

                cookieName = cookieHeader.AsSpan(0, separatorPos).ToString().Trim();

                var endCookiePos = cookieHeader.IndexOf(';', separatorPos);
                if (endCookiePos == -1)
                {
                    // Cookie value extends to end of header (no attributes)
                    cookieValue = cookieHeader.AsSpan(separatorPos + 1).ToString().Trim();
                }
                else
                {
                    // Cookie value ends at first semicolon (before attributes)
                    cookieValue = cookieHeader.AsSpan(separatorPos + 1, endCookiePos - separatorPos - 1).ToString().Trim();
                }

                // Remove surrounding quotes if present (some cookies have quoted values)
                if (cookieValue.Length >= 2 && cookieValue.StartsWith('"') && cookieValue.EndsWith('"'))
                {
                    cookieValue = cookieValue.Substring(1, cookieValue.Length - 2);
                }

                // DON'T decode URL encoding - preserve % characters as requested

                return true;
            }

            static bool TryParseCookieForDisplay(string cookieHeader, out string cookieName, out string cookieValue)
            {
                cookieName = null;
                cookieValue = null;

                if (string.IsNullOrEmpty(cookieHeader))
                {
                    return false;
                }

                var separatorPos = cookieHeader.IndexOf('=');
                if (separatorPos <= 0)
                {
                    // Invalid cookie, don't add it
                    return false;
                }

                cookieName = cookieHeader.AsSpan(0, separatorPos).ToString().Trim();

                var endCookiePos = cookieHeader.IndexOf(';', separatorPos);
                if (endCookiePos == -1)
                {
                    // Cookie value extends to end of header (no attributes)
                    cookieValue = cookieHeader.AsSpan(separatorPos + 1).ToString().Trim();
                }
                else
                {
                    // Cookie value ends at first semicolon (before attributes)
                    cookieValue = cookieHeader.AsSpan(separatorPos + 1, endCookiePos - separatorPos - 1).ToString().Trim();
                }

                // DON'T remove quotes or decode URL encoding for display - keep original format

                return true;
            }

            static string[] SplitSetCookieHeaders(string combinedHeader)
            {
                if (string.IsNullOrEmpty(combinedHeader))
                {
                    return Array.Empty<string>();
                }

                var result = new List<string>();
                var current = new StringBuilder();
                var inQuotes = false;
                var i = 0;

                while (i < combinedHeader.Length)
                {
                    var c = combinedHeader[i];

                    if (c == '"')
                    {
                        inQuotes = !inQuotes;
                        current.Append(c);
                    }
                    else if (c == ',' && !inQuotes)
                    {
                        // Check if this comma is part of a date (look for pattern like "expires=Thu, 01-Jan-1970")
                        // Look ahead to see if next non-whitespace character looks like start of new cookie
                        var nextIndex = i + 1;
                        while (nextIndex < combinedHeader.Length && char.IsWhiteSpace(combinedHeader[nextIndex]))
                            nextIndex++;

                        if (nextIndex < combinedHeader.Length)
                        {
                            // Look for cookie name pattern (alphanumeric followed by =)
                            var foundEquals = false;
                            var tempIndex = nextIndex;
                            while (tempIndex < combinedHeader.Length && !char.IsWhiteSpace(combinedHeader[tempIndex]) && combinedHeader[tempIndex] != '=')
                                tempIndex++;

                            if (tempIndex < combinedHeader.Length && combinedHeader[tempIndex] == '=')
                                foundEquals = true;

                            if (foundEquals)
                            {
                                // This comma separates cookies
                                result.Add(current.ToString().Trim());
                                current.Clear();
                            }
                            else
                            {
                                // This comma is part of a date or value
                                current.Append(c);
                            }
                        }
                        else
                        {
                            current.Append(c);
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }

                    i++;
                }

                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                }

                return result.ToArray();
            }

            // Unzip the GZipped content if still gzipped (after Content-Length calculation)
            if (data.RAWSOURCE.Length > 1 && data.RAWSOURCE[0] == 0x1F && data.RAWSOURCE[1] == 0x8B)
            {
                try
                {
                    data.RAWSOURCE = GZip.Unzip(data.RAWSOURCE);
                }
                catch
                {
                    data.Logger.Log("Tried to unzip but failed", LogColors.DarkOrange);
                }
            }

            // Source
            if (!string.IsNullOrWhiteSpace(requestOptions.CodePagesEncoding))
            {
                data.SOURCE = CodePagesEncodingProvider.Instance
                    .GetEncoding(requestOptions.CodePagesEncoding).GetString(data.RAWSOURCE);
            }
            else
            {
                data.SOURCE = Encoding.UTF8.GetString(data.RAWSOURCE);
            }

            if (requestOptions.DecodeHtml)
            {
                data.SOURCE = WebUtility.HtmlDecode(data.SOURCE);
            }

            if (!(status >= 300 && status < 400))
            {
                data.Logger.Log("Received Payload:", LogColors.ForestGreen);
                data.Logger.Log(data.SOURCE, LogColors.GreenYellow, true);
            }
        }

        // Helper method to merge a raw Cookie header into the shared cookie jar and then remove the header
        private static void MergeCookieHeader(IDictionary<string, string> headers, IDictionary<string, string> cookieJar)
        {
            // Find the Cookie header using case-insensitive lookup
            var cookieHeaderKey = headers.Keys.FirstOrDefault(k => k.Equals("Cookie", StringComparison.OrdinalIgnoreCase));

            if (cookieHeaderKey is null)
                return;

            var cookieHeaderValue = headers[cookieHeaderKey];

            // Split on semicolons; Instagram cookies don't contain semicolons inside values
            var cookiePairs = cookieHeaderValue.Split(';');

            foreach (var pair in cookiePairs)
            {
                var trimmed = pair.Trim();
                if (trimmed.Length == 0)
                    continue;

                var eqPos = trimmed.IndexOf('=');
                if (eqPos <= 0)
                    continue; // invalid cookie fragment

                var name = trimmed[..eqPos].Trim();
                var value = trimmed[(eqPos + 1)..].Trim();

                // If the value is wrapped in quotes, unquote it
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value[1..^1];

                cookieJar[name] = value;
            }

            // Remove the Cookie header so that only the consolidated jar is sent
            headers.Remove(cookieHeaderKey);
        }
    }
}
