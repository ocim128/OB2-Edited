using RuriLib.Functions.Http.Options;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Bots;
using RuriLib.Models.Proxies;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TlsClient.Core.Models.Entities;
using TlsClient.Core.Models.Requests;
using TlsClient.Core.Helpers;
using TlsClient.Native;

namespace RuriLib.Functions.Http
{
    /// <summary>
    /// HTTP request handler using TlsClient.NET for TLS fingerprint spoofing (JA3/JA4).
    /// This handler is ideal for bypassing anti-bot detection that uses TLS fingerprinting.
    /// Optimized for high-performance bulk requests with session reuse and connection pooling.
    /// </summary>
    internal class TlsClientRequestHandler : HttpRequestHandler
    {
        private static bool _initialized = false;
        private static readonly object _initLock = new();
        private static string _initError = null;

        // ============= SHARED CLIENT PATTERN (Like SystemNet) =============
        // Single shared client - the Go HTTP Transport handles connection pooling internally
        private static NativeTlsClient _sharedClient;
        private static long _requestCount = 0;

        // Pre-configured transport options for connection pooling (Go handles this internally)
        // Note: IdleConnTimeout must be null due to TimeSpan->time.Duration serialization issues
        private static readonly TransportOptions _defaultTransportOptions = new()
        {
            MaxIdleConns = 100,                              // Max idle connections across all hosts
            MaxIdleConnsPerHost = 10,                        // Max idle connections per host  
            MaxConnsPerHost = 0,                             // 0 = unlimited concurrent connections per host
            IdleConnTimeout = null,                          // null = use Go's default (90 seconds)
            DisableKeepAlives = false,                       // Enable HTTP keep-alive
            DisableCompression = false,                      // Enable compression
            ReadBufferSize = 4096,                           // Read buffer size
            WriteBufferSize = 4096,                          // Write buffer size
            MaxResponseHeaderBytes = 0                       // 0 = use default
        };

        /// <summary>
        /// Gets or sets the browser profile to emulate for TLS fingerprinting.
        /// </summary>
        public TlsClientIdentifier ClientProfile { get; set; } = TlsClientIdentifier.Chrome120;

        /// <summary>
        /// Gets or sets whether to skip TLS certificate verification.
        /// </summary>
        public bool InsecureSkipVerify { get; set; } = false;

        private static readonly Lazy<IReadOnlyDictionary<string, TlsClientIdentifier>> _tlsProfileMap = new(() =>
            typeof(TlsClientIdentifier).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(static f => f.FieldType == typeof(TlsClientIdentifier))
                .ToDictionary(static f => f.Name, static f => (TlsClientIdentifier)f.GetValue(null), StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Initialize the TLS client native library and create shared client. Must be called before any requests.
        /// </summary>
        private static void EnsureInitialized()
        {
            if (_initialized) return;

            lock (_initLock)
            {
                if (_initialized) return;

                try
                {
                    // Try to find the native library in common locations
                    var possiblePaths = GetNativeLibraryPaths();
                    string foundPath = null;

                    foreach (var path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            foundPath = path;
                            break;
                        }
                    }

                    if (foundPath == null)
                    {
                        _initError = $"TLS client native library not found. Searched paths:\n{string.Join("\n", possiblePaths)}";
                        throw new FileNotFoundException(_initError);
                    }

                    NativeTlsClient.Initialize(foundPath);
                    
                    // Create shared client instance for connection reuse (like SystemNet's sharedClient)
                    _sharedClient = new NativeTlsClient();
                    
                    _initialized = true;
                }
                catch (Exception ex)
                {
                    _initError = $"Failed to initialize TLS client: {ex.Message}";
                    throw;
                }
            }
        }

        private static IEnumerable<string> GetNativeLibraryPaths()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" : "x86";

            yield return Path.Combine(baseDir, "tls-client.dll");
            yield return Path.Combine(baseDir, "runtimes", "tls-client", "win", arch, "tls-client.dll");
            yield return Path.Combine(baseDir, "runtimes", "win-x64", "native", "tls-client.dll");
            
            // NuGet packages directory
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(userProfile, ".nuget", "packages", "tlsclient.native.win-x64", "1.9.1", "runtimes", "tls-client", "win", "x64", "tls-client.dll");
        }

        public override async Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
        {
            EnsureInitialized();

            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            var request = BuildRequest(data, options);
            
            // Set content if provided
            if (!string.IsNullOrEmpty(options.Content) || options.AlwaysSendContent)
            {
                request.RequestBody = options.Content ?? string.Empty;
                request.Headers["Content-Type"] = options.ContentType;
            }

            await ExecuteRequestAsync(data, options, request);
        }

        public override async Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
        {
            EnsureInitialized();

            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            var request = BuildRequest(data, options);

            if (options.Content?.Length > 0)
            {
                request.RequestBody = Convert.ToBase64String(options.Content);
                request.IsByteRequest = true;
                request.Headers["Content-Type"] = options.ContentType;
            }

            await ExecuteRequestAsync(data, options, request);
        }

        public override async Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
        {
            EnsureInitialized();

            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            var request = BuildRequest(data, options);

            // Add basic auth header
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
            request.Headers["Authorization"] = $"Basic {credentials}";

            await ExecuteRequestAsync(data, options, request);
        }

        public override async Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options)
        {
            EnsureInitialized();

            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            var request = BuildRequest(data, options);
            
            // Build multipart content
            var boundary = options.Boundary;
            if (string.IsNullOrEmpty(boundary))
            {
                boundary = $"----WebKitFormBoundary{Guid.NewGuid():N}";
            }

            var sb = new StringBuilder();
            foreach (var content in options.Contents)
            {
                switch (content)
                {
                    case StringHttpContent stringContent:
                        sb.Append($"--{boundary}\r\n");
                        sb.Append($"Content-Disposition: form-data; name=\"{stringContent.Name}\"\r\n");
                        if (!string.IsNullOrEmpty(stringContent.ContentType))
                        {
                            sb.Append($"Content-Type: {stringContent.ContentType}\r\n");
                        }
                        sb.Append("\r\n");
                        sb.Append(stringContent.Data);
                        sb.Append("\r\n");
                        break;

                    case RawHttpContent rawContent:
                        sb.Append($"--{boundary}\r\n");
                        sb.Append($"Content-Disposition: form-data; name=\"{rawContent.Name}\"\r\n");
                        if (!string.IsNullOrEmpty(rawContent.ContentType))
                        {
                            sb.Append($"Content-Type: {rawContent.ContentType}\r\n");
                        }
                        sb.Append("\r\n");
                        sb.Append(Encoding.UTF8.GetString(rawContent.Data));
                        sb.Append("\r\n");
                        break;

                    case FileHttpContent fileContent:
                        sb.Append($"--{boundary}\r\n");
                        sb.Append($"Content-Disposition: form-data; name=\"{fileContent.Name}\"; filename=\"{fileContent.FileName}\"\r\n");
                        sb.Append($"Content-Type: {fileContent.ContentType}\r\n");
                        sb.Append("\r\n");
                        // Read file content
                        if (File.Exists(fileContent.FileName))
                        {
                            sb.Append(File.ReadAllText(fileContent.FileName));
                        }
                        sb.Append("\r\n");
                        break;
                }
            }
            sb.Append($"--{boundary}--\r\n");

            request.RequestBody = sb.ToString();
            request.Headers["Content-Type"] = $"multipart/form-data; boundary={boundary}";

            await ExecuteRequestAsync(data, options, request);
        }

        private static TlsClientIdentifier ResolveTlsClientProfile(string profile, TlsClientIdentifier fallback, IBotLogger logger)
        {
            if (!string.IsNullOrWhiteSpace(profile) && _tlsProfileMap.Value.TryGetValue(profile, out var resolved))
            {
                return resolved;
            }

            if (!string.IsNullOrWhiteSpace(profile) && logger?.Enabled == true)
            {
                logger.Log($"[TLS Client] Unknown profile '{profile}', falling back to {fallback}", LogColors.Orange);
            }

            return fallback ?? TlsClientIdentifier.Chrome120;
        }

        private static string BuildProxyUrl(Proxy proxy)
        {
            if (proxy == null)
            {
                return null;
            }

            var scheme = proxy.Type switch
            {
                ProxyType.Http => "http",
                ProxyType.Socks4 => "socks4",
                ProxyType.Socks4a => "socks4a",
                ProxyType.Socks5 => "socks5",
                _ => "http"
            };

            if (proxy.NeedsAuthentication)
            {
                var user = Uri.EscapeDataString(proxy.Username ?? string.Empty);
                var pass = Uri.EscapeDataString(proxy.Password ?? string.Empty);
                return $"{scheme}://{user}:{pass}@{proxy.Host}:{proxy.Port}";
            }

            return $"{scheme}://{proxy.Host}:{proxy.Port}";
        }

        private Request BuildRequest(BotData data, Options.HttpRequestOptions options)
        {
            var tlsProfile = ResolveTlsClientProfile(options.TlsClientProfile, ClientProfile, data.Logger);
            var request = new Request
            {
                RequestUrl = options.Url,
                RequestMethod = new System.Net.Http.HttpMethod(options.Method.ToString()),
                TlsClientIdentifier = tlsProfile,
                FollowRedirects = options.AutoRedirect,
                TimeoutMilliseconds = options.TimeoutMilliseconds,
                TimeoutSeconds = 0,
                WithRandomTLSExtensionOrder = options.RandomizeTlsExtensionOrder,
                InsecureSkipVerify = options.InsecureSkipVerify || InsecureSkipVerify,
                WithDefaultCookieJar = true,              // Enable cookie jar for session persistence
                SessionId = data.TlsClientSessionId ??= Guid.NewGuid(),
                TransportOptions = _defaultTransportOptions, // Connection pooling settings
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                HeaderOrder = null
            };

            if (options.HttpLibrary == HttpLibrary.RuriLibHttp)
            {
                request.ForceHttp1 = true;
            }
            else if (!string.IsNullOrWhiteSpace(options.HttpVersion))
            {
                var version = options.HttpVersion.Trim();
                if (version.StartsWith("1", StringComparison.OrdinalIgnoreCase))
                {
                    request.ForceHttp1 = true;
                }
                else if (version.StartsWith("2", StringComparison.OrdinalIgnoreCase))
                {
                    request.ForceHttp1 = false;
                }
            }

            if (!string.IsNullOrWhiteSpace(options.CustomJa3String))
            {
                request.CustomTlsClient = new CustomTlsClient
                {
                    Ja3String = options.CustomJa3String
                };
            }

            // Add custom headers in order (header order matters for fingerprinting)
            foreach (var header in options.CustomHeaders)
            {
                request.Headers[header.Key] = header.Value;
            }

            // Let the TLS profile control header order unless explicitly needed.

            // Set cookies using RequestCookies (the proper way in TlsClient)
            // Note: Using Cookie header doesn't work reliably in TlsClient
            if (data.COOKIES?.Count > 0)
            {
                request.RequestCookies = data.COOKIES
                    .Select(c => new TlsClientCookie(c.Key, c.Value))
                    .ToList();
            }

            if (data.UseProxy && data.Proxy != null)
            {
                request.ProxyUrl = BuildProxyUrl(data.Proxy);
            }

            // Enable debug mode for detailed logging
            request.WithDebug = data.Logger?.Enabled == true;

            // Log request details
            if (data.Logger?.Enabled == true)
            {
                data.Logger.Log($"[TLS Client] Building request to {options.Url}", LogColors.DarkOrchid);
                data.Logger.Log($"[TLS Client] Using browser profile: {tlsProfile} (shared client)", LogColors.DarkOrchid);
                data.Logger.Log($"[TLS Client] Method: {options.Method}", LogColors.DarkOrchid);
                
                // Log headers being sent
                if (request.Headers?.Count > 0)
                {
                    var sbHeaders = new StringBuilder();
                    sbHeaders.AppendLine("[TLS Client] Request Headers:");
                    foreach (var header in request.Headers)
                    {
                        sbHeaders.AppendLine($"  {header.Key}: {header.Value}");
                    }
                    data.Logger.Log(sbHeaders.ToString(), LogColors.MediumPurple);
                }
                
                // Log cookies being sent
                if (request.RequestCookies?.Count > 0)
                {
                    var sbCookies = new StringBuilder();
                    sbCookies.AppendLine("[TLS Client] Request Cookies:");
                    foreach (var cookie in request.RequestCookies)
                    {
                        sbCookies.AppendLine($"  {cookie.Name}={cookie.Value}");
                    }
                    data.Logger.Log(sbCookies.ToString(), LogColors.Khaki);
                }
            }

            return request;
        }

        private async Task ExecuteRequestAsync(BotData data, Options.HttpRequestOptions options, Request request)
        {
            // Increment request counter
            var requestNumber = Interlocked.Increment(ref _requestCount);

            var startTime = DateTime.UtcNow;
            var logEnabled = data.Logger?.Enabled == true;
            
            // Log request body before execution (so we can see what's being sent)
            if (logEnabled && !string.IsNullOrEmpty(request.RequestBody))
            {
                data.Logger.Log("[TLS Client] Request Body (PostData):", LogColors.Gold);
                // For byte requests, show that it's base64 encoded
                if (request.IsByteRequest)
                {
                    data.Logger.Log($"  [Base64 Encoded - {request.RequestBody.Length} chars]", LogColors.Gold);
                    // Optionally decode and show first part for debugging
                    try
                    {
                        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(request.RequestBody));
                        var preview = decoded.Length > 500 ? decoded.Substring(0, 500) + "..." : decoded;
                        data.Logger.Log(preview, LogColors.GreenYellow, true);
                    }
                    catch { /* Ignore decode errors */ }
                }
                else
                {
                    var bodyPreview = request.RequestBody.Length > 2000 
                        ? request.RequestBody.Substring(0, 2000) + "..." 
                        : request.RequestBody;
                    data.Logger.Log(bodyPreview, LogColors.GreenYellow, true);
                }
            }
            
            try
            {
                // Use shared client with native async and cancellation token support
                var response = await _sharedClient.RequestAsync(request, data.CancellationToken)
                    .ConfigureAwait(false);

                // Native TLS client surfaces transport failures as status 0 or synthetic timeouts.
                var statusCode = (int)response.Status;
                if (statusCode == 0)
                {
                    var message = string.IsNullOrWhiteSpace(response.Body)
                        ? "TLS client request failed (status 0)."
                        : response.Body.Trim();
                    if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new TimeoutException(message);
                    }
                    throw new HttpRequestException(message);
                }
                else if (response.Status == HttpStatusCode.RequestTimeout)
                {
                    var message = response.Body ?? string.Empty;
                    var looksLikeTimeout = message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
                    var hasHeaders = response.Headers != null && response.Headers.Count > 0;
                    if (looksLikeTimeout && !hasHeaders)
                    {
                        throw new TimeoutException(string.IsNullOrWhiteSpace(message)
                            ? "TLS client request timed out."
                            : message.Trim());
                    }
                }

                var elapsed = DateTime.UtcNow - startTime;
                // Log response info with request tracking
                if (logEnabled)
                {
                    data.Logger.Log($"[TLS Client] Response: {response.Status} in {elapsed.TotalMilliseconds:F0}ms (Total requests: {requestNumber})", LogColors.DarkOrchid);
                }

                // Set response code
                data.RESPONSECODE = (int)response.Status;
                if (logEnabled)
                {
                    data.Logger.Log($"Response code: {data.RESPONSECODE}", LogColors.Citrine);
                }

                // Set address
                data.ADDRESS = options.Url;

                var responseBody = response.Body ?? string.Empty;
                string source = string.Empty;

                // Set raw source as byte array only if needed
                if (options.ReadResponseContent && responseBody.Length > 0)
                {
                    data.RAWSOURCE = Encoding.UTF8.GetBytes(responseBody);

                    if (!string.IsNullOrWhiteSpace(options.CodePagesEncoding))
                    {
                        source = CodePagesEncodingProvider.Instance
                            .GetEncoding(options.CodePagesEncoding)
                            .GetString(data.RAWSOURCE);
                    }
                    else
                    {
                        source = responseBody;
                    }

                    // Decode HTML entities if requested
                    if (options.DecodeHtml)
                    {
                        source = WebUtility.HtmlDecode(source);
                        data.RAWSOURCE = Encoding.UTF8.GetBytes(source);
                    }
                }
                else
                {
                    data.RAWSOURCE = Array.Empty<byte>();
                }

                data.SOURCE = source;

                // Parse headers
                data.HEADERS.Clear();
                if (!options.DisableHeaderParsing && response.Headers != null)
                {
                    foreach (var header in response.Headers)
                    {
                        // Handle header values (they come as List<string>)
                        if (header.Value != null && header.Value.Count > 0)
                        {
                            data.HEADERS[header.Key] = string.Join(", ", header.Value);
                        }
                    }
                }

                if (!data.HEADERS.ContainsKey("Content-Length"))
                {
                    data.HEADERS["Content-Length"] = data.RAWSOURCE.Length.ToString();
                }

                // Parse cookies from Set-Cookie headers
                if (!options.DisableCookieParsing && response.Headers != null)
                {
                    if (response.Headers.TryGetValue("Set-Cookie", out var setCookieValues) && setCookieValues != null)
                    {
                        foreach (var cookieHeader in setCookieValues)
                        {
                            try
                            {
                                ParseAndAddCookie(cookieHeader, data.COOKIES);
                            }
                            catch
                            {
                                // Ignore cookie parsing errors
                            }
                        }
                    }
                }

                // Log headers
                if (!options.DisableHeaderParsing && logEnabled)
                {
                    var sbHeaders = new StringBuilder();
                    sbHeaders.AppendLine("Received Headers:");
                    foreach (var header in data.HEADERS)
                    {
                        sbHeaders.AppendLine($"{header.Key}: {header.Value}");
                    }
                    data.Logger.Log(sbHeaders.ToString(), LogColors.Violet);
                }

                // Log cookies
                if (!options.DisableCookieParsing && logEnabled)
                {
                    var sbCookies = new StringBuilder();
                    sbCookies.AppendLine("Received Cookies:");
                    foreach (var cookie in data.COOKIES)
                    {
                        sbCookies.AppendLine($"{cookie.Key}: {cookie.Value}");
                    }
                    data.Logger.Log(sbCookies.ToString(), LogColors.Khaki);
                }

                // Log response body to match other HTTP handlers
                if (logEnabled && source.Length > 0)
                {
                    data.Logger.Log("Received Payload:", LogColors.ForestGreen);
                    data.Logger.Log(source, LogColors.GreenYellow, true);
                }
            }
            catch (OperationCanceledException) when (data.CancellationToken.IsCancellationRequested)
            {
                throw; // Re-throw cancellation
            }
            catch (Exception ex)
            {
                data.Logger?.Log($"[TLS Client] Request failed: {ex.Message}", LogColors.OrangeRed);
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ParseAndAddCookie(string cookieHeader, Dictionary<string, string> cookies)
        {
            if (string.IsNullOrEmpty(cookieHeader))
                return;

            var separatorPos = cookieHeader.IndexOf('=');
            if (separatorPos <= 0)
                return;

            var cookieName = cookieHeader.AsSpan(0, separatorPos).ToString().Trim();

            var endCookiePos = cookieHeader.IndexOf(';', separatorPos);
            string cookieValue;
            if (endCookiePos == -1)
            {
                cookieValue = cookieHeader.AsSpan(separatorPos + 1).ToString().Trim();
            }
            else
            {
                cookieValue = cookieHeader.AsSpan(separatorPos + 1, endCookiePos - separatorPos - 1).ToString().Trim();
            }

            // Remove surrounding quotes if present
            if (cookieValue.Length >= 2 && cookieValue.StartsWith('"') && cookieValue.EndsWith('"'))
            {
                cookieValue = cookieValue.Substring(1, cookieValue.Length - 2);
            }

            cookies[cookieName] = cookieValue;
        }

        /// <summary>
        /// Gets the total number of requests made by the shared client.
        /// </summary>
        public static long GetRequestCount() => Interlocked.Read(ref _requestCount);

        /// <summary>
        /// Destroys a TlsClient session by its session ID to free native memory.
        /// This should be called when a bot run completes to prevent memory leaks.
        /// </summary>
        /// <param name="sessionId">The session ID (GUID) to destroy</param>
        /// <returns>True if the session was destroyed successfully, false otherwise</returns>
        public static bool DestroySession(Guid sessionId)
        {
            if (!_initialized || _sharedClient == null)
            {
                return false;
            }

            try
            {
                // The TlsClient.Native library uses string session IDs
                // Call the static DestroySession method to remove the session from the Go native layer's session map
                var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { sessionId = sessionId.ToString() });
                TlsClient.Native.Wrappers.TlsClientWrapper.DestroySession(payload);
                return true;
            }
            catch
            {
                // Ignore destruction errors - session may already be gone
                return false;
            }
        }

        /// <summary>
        /// Destroys all TlsClient sessions to free native memory.
        /// This should be called when a job completes to prevent memory leaks.
        /// </summary>
        /// <returns>True if sessions were destroyed successfully, false otherwise</returns>
        public static bool DestroyAllSessions()
        {
            if (!_initialized || _sharedClient == null)
            {
                return false;
            }

            try
            {
                _sharedClient.DestroyAll();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
