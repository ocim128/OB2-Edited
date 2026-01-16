using RuriLib.Functions.Http.Options;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Bots;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TlsClient.Core.Models.Entities;
using TlsClient.Core.Models.Requests;
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

        private Request BuildRequest(BotData data, Options.HttpRequestOptions options)
        {
            var request = new Request
            {
                RequestUrl = options.Url,
                RequestMethod = new System.Net.Http.HttpMethod(options.Method.ToString()),
                TlsClientIdentifier = ClientProfile,
                FollowRedirects = options.AutoRedirect,
                TimeoutMilliseconds = options.TimeoutMilliseconds,
                TimeoutSeconds = 0,
                WithRandomTLSExtensionOrder = true,       // Randomize extension order to avoid detection
                InsecureSkipVerify = InsecureSkipVerify,  // Allow skipping cert verification if needed
                WithDefaultCookieJar = true,              // Enable cookie jar for session persistence
                TransportOptions = _defaultTransportOptions, // Connection pooling settings
                Headers = new Dictionary<string, string>()
            };

            // Add custom headers in order (header order matters for fingerprinting)
            foreach (var header in options.CustomHeaders)
            {
                request.Headers[header.Key] = header.Value;
            }

            // Set cookies
            if (data.COOKIES?.Count > 0)
            {
                var cookieString = string.Join("; ", data.COOKIES.Select(c => $"{c.Key}={c.Value}"));
                request.Headers["Cookie"] = cookieString;
            }

            // Log request details
            data.Logger?.Log($"[TLS Client] Building request to {options.Url}", LogColors.DarkOrchid);
            data.Logger?.Log($"[TLS Client] Using browser profile: {ClientProfile} (shared client)", LogColors.DarkOrchid);

            return request;
        }

        private async Task ExecuteRequestAsync(BotData data, Options.HttpRequestOptions options, Request request)
        {
            // Increment request counter
            var requestNumber = Interlocked.Increment(ref _requestCount);

            var startTime = DateTime.UtcNow;
            
            try
            {
                // Use shared client with native async and cancellation token support
                var response = await _sharedClient.RequestAsync(request, data.CancellationToken)
                    .ConfigureAwait(false);

                var elapsed = DateTime.UtcNow - startTime;

                // Log response info with request tracking
                data.Logger?.Log($"[TLS Client] Response: {response.Status} in {elapsed.TotalMilliseconds:F0}ms (Total requests: {requestNumber})", LogColors.DarkOrchid);

                // Set response code
                data.RESPONSECODE = (int)response.Status;

                // Set address
                data.ADDRESS = options.Url;

                // Set raw source as byte array
                if (!string.IsNullOrEmpty(response.Body))
                {
                    data.RAWSOURCE = Encoding.UTF8.GetBytes(response.Body);
                }
                else
                {
                    data.RAWSOURCE = Array.Empty<byte>();
                }

                // Parse headers
                data.HEADERS.Clear();
                if (response.Headers != null)
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
                if (!options.DisableHeaderParsing)
                {
                    var sbHeaders = new StringBuilder();
                    sbHeaders.AppendLine("Received Headers:");
                    foreach (var header in data.HEADERS)
                    {
                        sbHeaders.AppendLine($"{header.Key}: {header.Value}");
                    }
                    data.Logger?.Log(sbHeaders.ToString(), LogColors.Violet);
                }

                // Log cookies
                if (!options.DisableCookieParsing)
                {
                    var sbCookies = new StringBuilder();
                    sbCookies.AppendLine("Received Cookies:");
                    foreach (var cookie in data.COOKIES)
                    {
                        sbCookies.AppendLine($"{cookie.Key}: {cookie.Value}");
                    }
                    data.Logger?.Log(sbCookies.ToString(), LogColors.Khaki);
                }

                // Log response body preview
                if (data.RAWSOURCE.Length > 0)
                {
                    var responseString = Encoding.UTF8.GetString(data.RAWSOURCE);
                    
                    // Decode HTML entities if requested
                    if (options.DecodeHtml)
                    {
                        responseString = WebUtility.HtmlDecode(responseString);
                        data.RAWSOURCE = Encoding.UTF8.GetBytes(responseString);
                    }

                    var preview = responseString.Length > 500 ? responseString[..500] + "..." : responseString;
                    data.Logger?.Log($"Response: {preview}", LogColors.White);
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
    }
}
