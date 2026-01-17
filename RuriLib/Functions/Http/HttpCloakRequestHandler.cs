using HttpCloak;
using RuriLib.Extensions;
using RuriLib.Functions.Conversion;
using RuriLib.Functions.Http.Options;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Bots;
using RuriLib.Models.Proxies;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace RuriLib.Functions.Http
{
    internal class HttpCloakRequestHandler : HttpRequestHandler
    {
        private const string SessionObjectKey = "httpCloakSession";
        private const string DefaultPreset = "chrome-143";

        private sealed class SessionHolder : IDisposable
        {
            public SessionHolder(Session session, string key)
            {
                Session = session ?? throw new ArgumentNullException(nameof(session));
                Key = key ?? string.Empty;
            }

            public Session Session { get; }
            public string Key { get; }

            public void Dispose()
            {
                Session.Dispose();
            }
        }

        public override async Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
        {
            var headers = PrepareHeadersAndCookies(data, options);
            var body = BuildStringBody(options);
            if (body != null && !string.IsNullOrWhiteSpace(options.ContentType))
            {
                headers["Content-Type"] = options.ContentType;
            }

            data.Logger.LogHeader();
            LogRequest(data, options, headers, body);

            var response = await SendRequestAsync(data, options, headers, body, null, null).ConfigureAwait(false);
            HandleResponse(data, options, response);
        }

        public override async Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
        {
            var headers = PrepareHeadersAndCookies(data, options);
            var body = BuildRawBody(options);
            if (body != null && !string.IsNullOrWhiteSpace(options.ContentType))
            {
                headers["Content-Type"] = options.ContentType;
            }

            data.Logger.LogHeader();
            LogRequest(data, options, headers, body);

            var response = await SendRequestAsync(data, options, headers, null, body, null).ConfigureAwait(false);
            HandleResponse(data, options, response);
        }

        public override async Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
        {
            var headers = PrepareHeadersAndCookies(data, options);
            if (!string.IsNullOrEmpty(options.Username) || !string.IsNullOrEmpty(options.Password))
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
                headers["Authorization"] = $"Basic {credentials}";
            }

            data.Logger.LogHeader();
            LogRequest(data, options, headers, (string?)null);

            var response = await SendRequestAsync(data, options, headers, null, null, null).ConfigureAwait(false);
            HandleResponse(data, options, response);
        }

        public override async Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options)
        {
            var headers = PrepareHeadersAndCookies(data, options);
            var body = BuildMultipartBody(options);
            if (body != null)
            {
                headers["Content-Type"] = $"multipart/form-data; boundary={options.Boundary}";
            }

            data.Logger.LogHeader();
            LogMultipartRequest(data, options, headers);

            var response = await SendRequestAsync(data, options, headers, null, body, null).ConfigureAwait(false);
            HandleResponse(data, options, response);
        }

        private static string BuildHttpVersion(HttpRequestOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.HttpVersion))
            {
                return "auto";
            }

            var normalized = options.HttpVersion.Trim().ToLowerInvariant();
            return normalized switch
            {
                "auto" => "auto",
                "h1" => "h1",
                "h2" => "h2",
                "h3" => "h3",
                "http1" => "1.1",
                "http2" => "2",
                "http3" => "3",
                "1" => "1.1",
                "1.0" => "1.1",
                "1.1" => "1.1",
                "2" => "2",
                "2.0" => "2",
                "3" => "3",
                "3.0" => "3",
                _ => "auto"
            };
        }

        private static string GetDisplayHttpVersion(HttpRequestOptions options)
        {
            var normalized = BuildHttpVersion(options);
            return normalized switch
            {
                "auto" => "auto",
                "h1" => "1.1",
                "h2" => "2",
                "h3" => "3",
                "1.1" => "1.1",
                "2" => "2",
                "3" => "3",
                _ => options.HttpVersion ?? "1.1"
            };
        }

        private static string BuildPreset(HttpRequestOptions options)
        {
            return string.IsNullOrWhiteSpace(options.HttpCloakPreset)
                ? DefaultPreset
                : options.HttpCloakPreset.Trim();
        }

        private static int BuildTimeoutSeconds(HttpRequestOptions options)
        {
            if (options.TimeoutMilliseconds <= 0)
            {
                return 30;
            }

            return Math.Max(1, (int)Math.Ceiling(options.TimeoutMilliseconds / 1000.0));
        }

        private static Dictionary<string, string> PrepareHeadersAndCookies(BotData data, HttpRequestOptions options)
        {
            foreach (var cookie in options.CustomCookies)
            {
                data.COOKIES[cookie.Key] = cookie.Value;
            }

            var headers = new Dictionary<string, string>(options.CustomHeaders, StringComparer.OrdinalIgnoreCase);
            MergeCookieHeader(headers, data.COOKIES);
            return headers;
        }

        private static void MergeCookieHeader(IDictionary<string, string> headers, IDictionary<string, string> cookieJar)
        {
            if (headers.Count == 0)
            {
                return;
            }

            var cookieHeaderKey = headers.Keys.FirstOrDefault(k =>
                k.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("Cookies", StringComparison.OrdinalIgnoreCase));

            if (cookieHeaderKey == null)
            {
                return;
            }

            var cookieHeaderValue = headers[cookieHeaderKey];
            headers.Remove(cookieHeaderKey);

            if (string.IsNullOrWhiteSpace(cookieHeaderValue))
            {
                return;
            }

            foreach (var part in cookieHeaderValue.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                var idx = trimmed.IndexOf('=');
                if (idx <= 0)
                {
                    continue;
                }

                var name = trimmed.Substring(0, idx).Trim();
                var value = trimmed.Substring(idx + 1).Trim();
                if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                if (!string.IsNullOrEmpty(name))
                {
                    cookieJar[name] = value;
                }
            }
        }

        private static void SyncSessionCookies(Session session, IDictionary<string, string> cookies)
        {
            session.ClearCookies();

            foreach (var cookie in cookies)
            {
                if (!string.IsNullOrEmpty(cookie.Value))
                {
                    session.SetCookie(cookie.Key, cookie.Value);
                }
            }
        }

        private static string BuildSessionKey(HttpRequestOptions options, string? proxyUrl)
        {
            var preset = BuildPreset(options);
            var httpVersion = BuildHttpVersion(options);
            var timeoutSeconds = BuildTimeoutSeconds(options);
            var verify = (!options.InsecureSkipVerify).ToString();
            return $"{preset}|{httpVersion}|{proxyUrl ?? string.Empty}|{options.AutoRedirect}|{options.MaxNumberOfRedirects}|{timeoutSeconds}|{verify}";
        }

        private static SessionHolder GetOrCreateSession(BotData data, HttpRequestOptions options)
        {
            var proxyUrl = data.UseProxy ? BuildProxyUrl(data.Proxy) : null;
            var sessionKey = BuildSessionKey(options, proxyUrl);
            var holder = data.TryGetObject<SessionHolder>(SessionObjectKey);

            if (holder != null && holder.Key == sessionKey)
            {
                return holder;
            }

            var session = CreateSession(options, proxyUrl, BuildPreset(options), BuildHttpVersion(options));

            holder = new SessionHolder(session, sessionKey);
            data.SetObject(SessionObjectKey, holder);
            return holder;
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

        private static string? BuildStringBody(StandardHttpRequestOptions options)
        {
            var content = options.Content ?? string.Empty;

            if (options.UrlEncodeContent)
            {
                content = string.Join("", content.SplitInChunks(2080)
                        .Select(Uri.EscapeDataString))
                    .Replace("%26", "&")
                    .Replace("%3D", "=");
            }

            content = content.Unescape();

            if (content.Length == 0 && !options.AlwaysSendContent)
            {
                return null;
            }

            return content;
        }

        private static byte[]? BuildRawBody(RawHttpRequestOptions options)
        {
            var content = options.Content ?? Array.Empty<byte>();
            if (content.Length == 0 && !options.AlwaysSendContent)
            {
                return null;
            }

            return content;
        }

        private static byte[]? BuildMultipartBody(MultipartHttpRequestOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.Boundary))
            {
                options.Boundary = GenerateMultipartBoundary();
            }

            if (options.Contents.Count == 0 && !options.AlwaysSendContent)
            {
                return null;
            }

            using var stream = new MemoryStream();
            var boundaryBytes = Encoding.UTF8.GetBytes($"--{options.Boundary}\r\n");
            var trailerBytes = Encoding.UTF8.GetBytes($"--{options.Boundary}--\r\n");

            foreach (var content in options.Contents)
            {
                stream.Write(boundaryBytes, 0, boundaryBytes.Length);

                switch (content)
                {
                    case StringHttpContent stringContent:
                        WriteHeader(stream, $"Content-Disposition: form-data; name=\"{stringContent.Name}\"\r\n");
                        WriteContentType(stream, stringContent.ContentType);
                        WriteHeader(stream, "\r\n");
                        WriteString(stream, stringContent.Data ?? string.Empty);
                        WriteHeader(stream, "\r\n");
                        break;

                    case RawHttpContent rawContent:
                        WriteHeader(stream, $"Content-Disposition: form-data; name=\"{rawContent.Name}\"\r\n");
                        WriteContentType(stream, rawContent.ContentType);
                        WriteHeader(stream, "\r\n");
                        if (rawContent.Data?.Length > 0)
                        {
                            stream.Write(rawContent.Data, 0, rawContent.Data.Length);
                        }
                        WriteHeader(stream, "\r\n");
                        break;

                    case FileHttpContent fileContent:
                        WriteHeader(stream, $"Content-Disposition: form-data; name=\"{fileContent.Name}\"; filename=\"{Path.GetFileName(fileContent.FileName)}\"\r\n");
                        WriteContentType(stream, fileContent.ContentType);
                        WriteHeader(stream, "\r\n");
                        if (File.Exists(fileContent.FileName))
                        {
                            var fileBytes = File.ReadAllBytes(fileContent.FileName);
                            stream.Write(fileBytes, 0, fileBytes.Length);
                        }
                        WriteHeader(stream, "\r\n");
                        break;
                }
            }

            stream.Write(trailerBytes, 0, trailerBytes.Length);
            return stream.ToArray();
        }

        private static void WriteHeader(Stream stream, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteString(Stream stream, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteContentType(Stream stream, string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return;
            }

            WriteHeader(stream, $"Content-Type: {contentType}\r\n");
        }

        private static Session CreateSession(HttpRequestOptions options, string? proxyUrl, string preset, string httpVersion)
            => new Session(
                preset: preset,
                proxy: proxyUrl,
                timeout: BuildTimeoutSeconds(options),
                httpVersion: httpVersion,
                verify: !options.InsecureSkipVerify,
                allowRedirects: options.AutoRedirect,
                maxRedirects: options.MaxNumberOfRedirects);

        private static Response ExecuteRequest(
            Session session,
            HttpRequestOptions options,
            Dictionary<string, string> headers,
            string? body,
            byte[]? bodyBytes,
            (string Username, string Password)? auth)
        {
            if (bodyBytes != null)
            {
                return session.RequestBinary(options.Method.ToString(), options.Url, bodyBytes, headers, BuildTimeoutSeconds(options), auth);
            }

            return session.Request(options.Method.ToString(), options.Url, body, headers, BuildTimeoutSeconds(options), auth);
        }

        private async Task<Response> SendRequestAsync(
            BotData data,
            HttpRequestOptions options,
            Dictionary<string, string> headers,
            string? body,
            byte[]? bodyBytes,
            (string Username, string Password)? auth)
        {
            data.CancellationToken.ThrowIfCancellationRequested();

            var holder = GetOrCreateSession(data, options);
            SyncSessionCookies(holder.Session, data.COOKIES);

            try
            {
                return await Task.Run(() =>
                    ExecuteRequest(holder.Session, options, headers, body, bodyBytes, auth),
                    data.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (data.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                data.Logger?.Log($"[HttpCloak] Request failed: {ex.Message}", LogColors.OrangeRed);
                throw;
            }
        }

        private static void HandleResponse(BotData data, HttpRequestOptions options, Response response)
        {
            data.ADDRESS = string.IsNullOrWhiteSpace(response.Url) ? options.Url : response.Url;
            data.RESPONSECODE = response.StatusCode;
            data.Logger.Log($"Address: {data.ADDRESS}", LogColors.DodgerBlue);
            data.Logger.Log($"Response code: {data.RESPONSECODE}", LogColors.Citrine);

            var responseBody = response.Text ?? string.Empty;
            if (options.ReadResponseContent && responseBody.Length > 0)
            {
                data.RAWSOURCE = Encoding.UTF8.GetBytes(responseBody);
            }
            else
            {
                data.RAWSOURCE = Array.Empty<byte>();
            }

            if (!string.IsNullOrWhiteSpace(options.CodePagesEncoding))
            {
                data.SOURCE = CodePagesEncodingProvider.Instance
                    .GetEncoding(options.CodePagesEncoding)
                    .GetString(data.RAWSOURCE);
            }
            else
            {
                data.SOURCE = options.ReadResponseContent ? responseBody : string.Empty;
            }

            if (options.DecodeHtml)
            {
                data.SOURCE = WebUtility.HtmlDecode(data.SOURCE);
                data.RAWSOURCE = Encoding.UTF8.GetBytes(data.SOURCE);
            }

            var logEnabled = data.Logger?.Enabled == true;

            if (!options.DisableHeaderParsing)
            {
                data.HEADERS.Clear();
                if (response.Headers != null)
                {
                    foreach (var header in response.Headers)
                    {
                        if (header.Value != null && header.Value.Length > 0)
                        {
                            data.HEADERS[header.Key] = string.Join(", ", header.Value);
                        }
                    }
                }

                if (!data.HEADERS.ContainsKey("Content-Length"))
                {
                    data.HEADERS["Content-Length"] = data.RAWSOURCE.Length.ToString();
                }

                if (logEnabled)
                {
                    var sbHeaders = new StringBuilder();
                    sbHeaders.AppendLine("Received Headers:");
                    foreach (var header in data.HEADERS)
                    {
                        sbHeaders.AppendLine($"{header.Key}: {header.Value}");
                    }
                    data.Logger.Log(sbHeaders.ToString(), LogColors.Violet);
                }
            }
            else
            {
                data.HEADERS.Clear();
                data.HEADERS["Content-Length"] = data.RAWSOURCE.Length.ToString();
                data.Logger?.Log("Header Parsing Skipped", LogColors.Orange);
            }

            if (!options.DisableCookieParsing)
            {
                if (response.Cookies != null)
                {
                    foreach (var cookie in response.Cookies)
                    {
                        if (!string.IsNullOrEmpty(cookie.Name))
                        {
                            data.COOKIES[cookie.Name] = cookie.Value ?? string.Empty;
                        }
                    }
                }

                if (logEnabled)
                {
                    var sbCookies = new StringBuilder();
                    sbCookies.AppendLine("Received Cookies:");
                    foreach (var cookie in data.COOKIES)
                    {
                        sbCookies.AppendLine($"{cookie.Key}: {cookie.Value}");
                    }
                    data.Logger.Log(sbCookies.ToString(), LogColors.Khaki);
                }
            }
            else
            {
                data.Logger?.Log("Cookie Parsing Skipped", LogColors.Orange);
            }

            if (logEnabled && data.SOURCE.Length > 0)
            {
                data.Logger.Log("Received Payload:", LogColors.ForestGreen);
                data.Logger.Log(data.SOURCE, LogColors.GreenYellow, true);
            }
        }

        private static void LogRequest(BotData data, HttpRequestOptions options, Dictionary<string, string> headers, string? body)
        {
            if (data.Logger?.Enabled != true)
            {
                return;
            }

            var uri = new Uri(options.Url);
            var sb = new StringBuilder();
            sb.AppendLine($"{options.Method} {uri.PathAndQuery} HTTP/{GetDisplayHttpVersion(options)}");
            sb.AppendLine($"Host: {uri.Host}");

            foreach (var header in headers)
            {
                sb.AppendLine($"{header.Key}: {header.Value}");
            }

            var cookieHeader = string.Join("; ", data.COOKIES.Where(static c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}"));
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                sb.AppendLine($"Cookie: {cookieHeader}");
            }

            if (body != null)
            {
                sb.AppendLine($"Content-Length: {Encoding.UTF8.GetByteCount(body)}");
                sb.AppendLine();
                sb.AppendLine(body);
            }

            data.Logger.Log(sb.ToString(), LogColors.NonPhotoBlue);
        }

        private static void LogRequest(BotData data, HttpRequestOptions options, Dictionary<string, string> headers, byte[]? body)
        {
            if (data.Logger?.Enabled != true)
            {
                return;
            }

            var uri = new Uri(options.Url);
            var sb = new StringBuilder();
            sb.AppendLine($"{options.Method} {uri.PathAndQuery} HTTP/{GetDisplayHttpVersion(options)}");
            sb.AppendLine($"Host: {uri.Host}");

            foreach (var header in headers)
            {
                sb.AppendLine($"{header.Key}: {header.Value}");
            }

            var cookieHeader = string.Join("; ", data.COOKIES.Where(static c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}"));
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                sb.AppendLine($"Cookie: {cookieHeader}");
            }

            if (body != null)
            {
                sb.AppendLine($"Content-Length: {body.Length}");
                sb.AppendLine();
                sb.AppendLine(Base64Converter.ToBase64String(body));
            }

            data.Logger.Log(sb.ToString(), LogColors.NonPhotoBlue);
        }

        private static void LogMultipartRequest(BotData data, MultipartHttpRequestOptions options, Dictionary<string, string> headers)
        {
            if (data.Logger?.Enabled != true)
            {
                return;
            }

            var uri = new Uri(options.Url);
            var sb = new StringBuilder();
            sb.AppendLine($"{options.Method} {uri.PathAndQuery} HTTP/{GetDisplayHttpVersion(options)}");
            sb.AppendLine($"Host: {uri.Host}");

            foreach (var header in headers)
            {
                sb.AppendLine($"{header.Key}: {header.Value}");
            }

            var cookieHeader = string.Join("; ", data.COOKIES.Where(static c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}"));
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                sb.AppendLine($"Cookie: {cookieHeader}");
            }

            if (!string.IsNullOrWhiteSpace(options.Boundary))
            {
                sb.AppendLine("Content-Length: (not calculated)");
                sb.AppendLine();
                sb.AppendLine(SerializeMultipart(options.Boundary, options.Contents));
            }

            data.Logger.Log(sb.ToString(), LogColors.NonPhotoBlue);
        }
    }
}
