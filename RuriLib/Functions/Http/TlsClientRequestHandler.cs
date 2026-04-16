using RuriLib.Functions.Files;
using RuriLib.Functions.Http.Options;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Bots;
using RuriLib.Models.Proxies;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TlsClient.Core.Models.Entities;
using TlsClient.Core.Models.Requests;
using TlsClient.Core.Models.Responses;
using TlsClient.Native;

namespace RuriLib.Functions.Http
{
    /// <summary>
    /// HTTP request handler using TlsClient.NET for TLS fingerprint spoofing (JA3/JA4).
    /// </summary>
    internal class TlsClientRequestHandler : HttpRequestHandler
    {
        private static bool initialized;
        private static readonly object initLock = new();
        private static string? initError;
        private static NativeTlsClient? sharedClient;
        private static long requestCount;

        private static readonly TransportOptions defaultTransportOptions = new()
        {
            MaxIdleConns = 100,
            MaxIdleConnsPerHost = 10,
            MaxConnsPerHost = 0,
            IdleConnTimeout = null,
            DisableKeepAlives = false,
            DisableCompression = false,
            ReadBufferSize = 4096,
            WriteBufferSize = 4096,
            MaxResponseHeaderBytes = 0
        };

        public TlsClientIdentifier ClientProfile { get; set; } = TlsClientIdentifier.Chrome120;

        public bool InsecureSkipVerify { get; set; }

        private static readonly Lazy<IReadOnlyDictionary<string, TlsClientIdentifier>> tlsProfileMap = new(() =>
            typeof(TlsClientIdentifier).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(static f => f.FieldType == typeof(TlsClientIdentifier))
                .ToDictionary(static f => f.Name, static f => (TlsClientIdentifier)f.GetValue(null)!, StringComparer.OrdinalIgnoreCase));

        public override Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
        {
            EnsureInitialized();
            return ExecutePipelineAsync(
                data,
                CreateNormalizedRequest(data, options),
                "TlsClient",
                (request, token) => SendAsync(data, options, request, token));
        }

        public override Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
        {
            EnsureInitialized();
            return ExecutePipelineAsync(
                data,
                CreateNormalizedRequest(data, options),
                "TlsClient",
                (request, token) => SendAsync(data, options, request, token));
        }

        public override Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
        {
            EnsureInitialized();
            return ExecutePipelineAsync(
                data,
                CreateNormalizedRequest(data, options),
                "TlsClient",
                (request, token) => SendAsync(data, options, request, token));
        }

        public override Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options)
        {
            EnsureInitialized();
            return ExecutePipelineAsync(
                data,
                CreateNormalizedRequest(data, options),
                "TlsClient",
                (request, token) => SendAsync(data, options, request, token));
        }

        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            lock (initLock)
            {
                if (initialized)
                {
                    return;
                }

                var possiblePaths = GetNativeLibraryPaths();
                var foundPath = possiblePaths.FirstOrDefault(File.Exists);
                if (foundPath == null)
                {
                    initError = $"TLS client native library not found. Searched paths:{Environment.NewLine}{string.Join(Environment.NewLine, possiblePaths)}";
                    throw new FileNotFoundException(initError);
                }

                try
                {
                    NativeTlsClient.Initialize(foundPath);
                    sharedClient = new NativeTlsClient();
                    initialized = true;
                }
                catch (Exception ex)
                {
                    initError = $"Failed to initialize TLS client: {ex.Message}";
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

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(userProfile, ".nuget", "packages", "tlsclient.native.win-x64", "1.9.1", "runtimes", "tls-client", "win", "x64", "tls-client.dll");
        }

        private async Task<NormalizedHttpResponse> SendAsync(
            BotData data,
            RuriLib.Functions.Http.Options.HttpRequestOptions options,
            NormalizedHttpRequest request,
            CancellationToken cancellationToken)
        {
            if (sharedClient is null)
            {
                throw new InvalidOperationException(initError ?? "TLS client is not initialized.");
            }

            var transportRequest = BuildRequest(data, options, request);
            var response = await sharedClient.RequestAsync(transportRequest, cancellationToken).ConfigureAwait(false);
            ValidateTlsResponse(response);

            var statusCode = (int)response.Status;
            var body = request.ReadResponseContent && (statusCode < 300 || statusCode >= 400)
                ? Encoding.UTF8.GetBytes(response.Body ?? string.Empty)
                : Array.Empty<byte>();
            var headers = NormalizeTlsHeaders(response.Headers);
            var address = Uri.TryCreate(response.Target, UriKind.Absolute, out var targetUri)
                ? targetUri
                : request.Uri;

            if (data.Logger?.Enabled == true)
            {
                var requestNumber = Interlocked.Increment(ref requestCount);
                data.Logger.Log($"[TLS Client] Response: {response.Status} (Total requests: {requestNumber})", LogColors.DarkOrchid);
            }

            return CreateResponseSnapshot(address, statusCode, headers, body);
        }

        private Request BuildRequest(BotData data, RuriLib.Functions.Http.Options.HttpRequestOptions options, NormalizedHttpRequest request)
        {
            var tlsProfile = ResolveTlsClientProfile(options.TlsClientProfile, ClientProfile, data.Logger);

            var transportRequest = new Request
            {
                RequestUrl = request.Uri.ToString(),
                RequestMethod = request.Method,
                TlsClientIdentifier = tlsProfile,
                FollowRedirects = false,
                TimeoutMilliseconds = request.TimeoutMilliseconds > 0 ? request.TimeoutMilliseconds : int.MaxValue,
                TimeoutSeconds = 0,
                WithRandomTLSExtensionOrder = options.RandomizeTlsExtensionOrder,
                InsecureSkipVerify = options.InsecureSkipVerify || InsecureSkipVerify,
                WithDefaultCookieJar = true,
                SessionId = data.TlsClientSessionId ??= Guid.NewGuid(),
                TransportOptions = defaultTransportOptions,
                Headers = CopyHeaders(request)
            };

            if (options.HttpLibrary == HttpLibrary.RuriLibHttp)
            {
                transportRequest.ForceHttp1 = true;
            }

            if (!string.IsNullOrWhiteSpace(options.CustomJa3String))
            {
                transportRequest.CustomTlsClient = new CustomTlsClient
                {
                    Ja3String = options.CustomJa3String
                };
            }

            if (transportRequest.Headers.Count > 0)
            {
                transportRequest.HeaderOrder = transportRequest.Headers.Keys.ToList();
            }

            transportRequest.RequestCookies = request.Cookies
                .Where(static c => !string.IsNullOrEmpty(c.Value))
                .Select(c => new TlsClientCookie(c.Key, c.Value))
                .ToList();

            if (data.UseProxy && data.Proxy != null)
            {
                transportRequest.ProxyUrl = BuildProxyUrl(data.Proxy);
            }

            if (request.MultipartContents != null)
            {
                transportRequest.RequestBody = BuildMultipartBody(data, request.MultipartContents, request.Boundary!);
                transportRequest.Headers["Content-Type"] = $"multipart/form-data; boundary={request.Boundary}";
            }
            else if (request.RawBody != null)
            {
                transportRequest.RequestBody = Convert.ToBase64String(request.RawBody);
                transportRequest.IsByteRequest = true;
                transportRequest.Headers["Content-Type"] = request.ContentType ?? "application/octet-stream";
            }
            else if (request.StringBody != null)
            {
                transportRequest.RequestBody = request.StringBody;
                if (!string.IsNullOrWhiteSpace(request.ContentType))
                {
                    transportRequest.Headers["Content-Type"] = request.ContentType;
                }
            }

            transportRequest.WithDebug = data.Logger?.Enabled == true;
            return transportRequest;
        }

        private static TlsClientIdentifier ResolveTlsClientProfile(string profile, TlsClientIdentifier fallback, IBotLogger logger)
        {
            if (!string.IsNullOrWhiteSpace(profile) && tlsProfileMap.Value.TryGetValue(profile, out var resolved))
            {
                return resolved;
            }

            if (!string.IsNullOrWhiteSpace(profile) && logger?.Enabled == true)
            {
                logger.Log($"[TLS Client] Unknown profile '{profile}', falling back to {fallback}", LogColors.Orange);
            }

            return fallback ?? TlsClientIdentifier.Chrome120;
        }

        private static string? BuildProxyUrl(Proxy? proxy)
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

        private static string BuildMultipartBody(BotData data, List<MyHttpContent> contents, string boundary)
        {
            var sb = new StringBuilder();

            foreach (var content in contents)
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
                        sb.Append($"Content-Disposition: form-data; name=\"{fileContent.Name}\"; filename=\"{Path.GetFileName(fileContent.FileName)}\"\r\n");
                        sb.Append($"Content-Type: {fileContent.ContentType}\r\n");
                        sb.Append("\r\n");

                        if (data.Providers.Security.RestrictBlocksToCWD)
                        {
                            FileUtils.ThrowIfNotInCWD(fileContent.FileName);
                        }

                        if (File.Exists(fileContent.FileName))
                        {
                            sb.Append(File.ReadAllText(fileContent.FileName));
                        }

                        sb.Append("\r\n");
                        break;
                }
            }

            sb.Append($"--{boundary}--\r\n");
            return sb.ToString();
        }

        private static Dictionary<string, List<string>> NormalizeTlsHeaders(Dictionary<string, List<string>>? headers)
        {
            if (headers == null)
            {
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }

            var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in headers)
            {
                if (!normalized.TryGetValue(header.Key, out var values))
                {
                    values = new List<string>();
                    normalized[header.Key] = values;
                }

                if (header.Value != null)
                {
                    values.AddRange(header.Value);
                }
            }

            return normalized;
        }

        private static void ValidateTlsResponse(Response response)
        {
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

            if (response.Status != System.Net.HttpStatusCode.RequestTimeout)
            {
                return;
            }

            var messageText = response.Body ?? string.Empty;
            var looksLikeTimeout = messageText.Contains("timeout", StringComparison.OrdinalIgnoreCase);
            var hasHeaders = response.Headers != null && response.Headers.Count > 0;
            if (looksLikeTimeout && !hasHeaders)
            {
                throw new TimeoutException(string.IsNullOrWhiteSpace(messageText)
                    ? "TLS client request timed out."
                    : messageText.Trim());
            }
        }

        public static long GetRequestCount() => Interlocked.Read(ref requestCount);

        public static bool DestroySession(Guid sessionId)
        {
            if (!initialized || sharedClient == null)
            {
                return false;
            }

            try
            {
                var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { sessionId = sessionId.ToString() });
                TlsClient.Native.Wrappers.TlsClientWrapper.DestroySession(payload);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool DestroyAllSessions()
        {
            if (!initialized || sharedClient == null)
            {
                return false;
            }

            try
            {
                sharedClient.DestroyAll();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
