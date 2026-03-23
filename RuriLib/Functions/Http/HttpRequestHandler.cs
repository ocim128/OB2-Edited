using RuriLib.Functions.Http.Options;
using RuriLib.Functions.Files;
using RuriLib.Extensions;
using RuriLib.Helpers;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Bots;
using RuriLib.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Functions.Http
{
    internal abstract class HttpRequestHandler
    {
        protected static readonly string[] commaHeaders = new[] { "Accept", "Accept-Encoding" };
        protected static readonly string[] cookieHeaderNames = new[] { "Set-Cookie", "Set-Cookie2" };

        public virtual Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
            => throw new NotImplementedException();
        public virtual Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
            => throw new NotImplementedException();
        public virtual Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
            => throw new NotImplementedException();
        public virtual Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options)
            => throw new NotImplementedException();

        /// <summary>
        /// Generates a random string to be used for boundary.
        /// </summary>
        protected static string GenerateMultipartBoundary()
        {
            var builder = new StringBuilder();
            var random = new Random();
            for (var i = 0; i < 16; i++)
            {
                var ch = Convert.ToChar(Convert.ToInt32(Math.Floor(26 * random.NextDouble() + 65)));
                builder.Append(ch);
            }
            return $"------WebKitFormBoundary{builder.ToString().ToLower()}";
        }

        protected static StreamContent CreateFileContent(Stream stream, string fieldName, string fileName, string contentType)
        {
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = $"\"{fieldName}\"",
                FileName = $"\"{fileName}\""
            }; // the extra quotes are key here
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return fileContent;
        }

        protected static string GetMediaHeaderString(string contentType)
            => new MediaTypeHeaderValue(contentType).ToString();

        protected static string SerializeMultipart(string boundary, List<MyHttpContent> contents)
        {
            using var writer = new StringWriter();

            foreach (var content in contents)
            {
                writer.WriteLine(boundary);

                switch (content)
                {
                    case StringHttpContent x:
                        writer.WriteLine($"Content-Disposition: form-data; name={x.Name}");
                        writer.WriteLine($"Content-Type: {GetMediaHeaderString(x.ContentType)}");
                        writer.WriteLine();
                        writer.WriteLine(x.Data);
                        break;

                    case RawHttpContent x:
                        writer.WriteLine($"Content-Disposition: form-data; name={x.Name}");
                        writer.WriteLine($"Content-Type: {GetMediaHeaderString(x.ContentType)}");
                        writer.WriteLine();
                        writer.WriteLine(Encoding.UTF8.GetString(x.Data));
                        break;

                    case FileHttpContent x:
                        writer.WriteLine($"Content-Disposition: form-data; name=\"{x.Name}\"; filename=\"{Path.GetFileName(x.FileName)}\"");
                        writer.WriteLine($"Content-Type: {GetMediaHeaderString(x.ContentType)}");
                        writer.WriteLine();
                        writer.WriteLine("[FILE CONTENTS NOT LOGGED]");
                        break;
                }
            }

            writer.WriteLine(boundary);

            return writer.ToString();
        }

        protected static TlsCipherSuite[] ParseCipherSuites(List<string> cipherSuites)
        {
            if (cipherSuites == null)
            {
                return Array.Empty<TlsCipherSuite>();
            }

            var parsed = new List<TlsCipherSuite>();

            foreach (var suite in cipherSuites)
            {
                try
                {
                    parsed.Add(Enum.Parse<TlsCipherSuite>(suite));
                }
                catch
                {
                    throw new NotSupportedException($"Cipher suite not supported: {suite}");
                }
            }

            return parsed.ToArray();
        }

        protected static HttpOptions GetClientOptions(BotData data, Options.HttpRequestOptions options) => new()
        {
            ConnectTimeout = data.Providers.ProxySettings.ConnectTimeout,
            ReadWriteTimeout = data.Providers.ProxySettings.ReadWriteTimeout,
            AutoRedirect = options.AutoRedirect,
            MaxNumberOfRedirects = options.MaxNumberOfRedirects,
            SecurityProtocol = options.SecurityProtocol,
            UseCustomCipherSuites = options.UseCustomCipherSuites,
            CustomCipherSuites = ParseCipherSuites(options.CustomCipherSuites),
            CertRevocationMode = data.Providers.Security.X509RevocationMode,
            ReadResponseContent = options.ReadResponseContent
        };

        protected static NormalizedHttpRequest CreateNormalizedRequest(BotData data, StandardHttpRequestOptions options)
        {
            var request = CreateBaseNormalizedRequest(data, options);
            var content = options.Content;

            if (!string.IsNullOrEmpty(content) || options.AlwaysSendContent)
            {
                if (options.UrlEncodeContent)
                {
                    content = string.Join("", content.SplitInChunks(2080)
                        .Select(Uri.EscapeDataString))
                        .Replace("%26", "&")
                        .Replace("%3D", "=");
                }

                request.StringBody = content;
                request.LoggedContent = content;
                request.ContentType = options.ContentType;
                request.ContentLengthDisplay = Encoding.UTF8.GetByteCount((content ?? string.Empty).Unescape()).ToString();
            }

            return request;
        }

        protected static NormalizedHttpRequest CreateNormalizedRequest(BotData data, RawHttpRequestOptions options)
        {
            var request = CreateBaseNormalizedRequest(data, options);
            request.RawBody = options.Content ?? Array.Empty<byte>();
            request.LoggedContent = Convert.ToBase64String(request.RawBody);
            request.ContentType = options.ContentType;
            request.ContentLengthDisplay = request.RawBody.Length.ToString();
            return request;
        }

        protected static NormalizedHttpRequest CreateNormalizedRequest(BotData data, BasicAuthHttpRequestOptions options)
        {
            var request = CreateBaseNormalizedRequest(data, options);
            request.RedirectAuthorization = "Basic " + Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
            request.Headers["Authorization"] = request.RedirectAuthorization;
            return request;
        }

        protected static NormalizedHttpRequest CreateNormalizedRequest(BotData data, MultipartHttpRequestOptions options)
        {
            var request = CreateBaseNormalizedRequest(data, options);
            request.Boundary = string.IsNullOrWhiteSpace(options.Boundary)
                ? GenerateMultipartBoundary()
                : options.Boundary;
            request.MultipartContents = options.Contents;
            request.ContentType = $"multipart/form-data; boundary=\"{request.Boundary}\"";
            request.ContentLengthDisplay = "(not calculated)";
            request.LoggedContent = SerializeMultipart(request.Boundary, options.Contents);
            return request;
        }

        protected async Task ExecutePipelineAsync(
            BotData data,
            NormalizedHttpRequest request,
            string transportName,
            Func<NormalizedHttpRequest, CancellationToken, Task<NormalizedHttpResponse>> sendAsync)
        {
            ValidateRequest(request);

            while (true)
            {
                data.Logger.LogHeader();
                LogHttpRequestData(data, request);

                try
                {
                    Activity.Current = null;
                    using var linkedCts = CreateLinkedTimeoutTokenSource(data.CancellationToken, request.TimeoutMilliseconds);

                    var response = await sendAsync(request, linkedCts.Token).ConfigureAwait(false);
                    ApplyResponseData(data, response, request);

                    if (!TryCreateRedirectRequest(request, response, out var redirectRequest))
                    {
                        return;
                    }

                    request = redirectRequest;
                }
                catch (Exception ex)
                {
                    LogHttpException(data, request, ex, transportName);
                    throw;
                }
            }
        }

        private static CancellationTokenSource CreateLinkedTimeoutTokenSource(CancellationToken parentToken, int timeoutMilliseconds)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            if (timeoutMilliseconds > 0)
            {
                linked.CancelAfter(timeoutMilliseconds);
            }

            return linked;
        }

        protected static Dictionary<string, string> PrepareHeadersAndCookies(
            BotData data,
            Options.HttpRequestOptions options)
        {
            foreach (var cookie in options.CustomCookies)
            {
                data.COOKIES[cookie.Key] = cookie.Value;
            }

            var headers = options.CustomHeaders?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MergeCookieHeader(headers, data.COOKIES);
            return headers;
        }

        protected static bool IsLikelyNetworkException(Exception ex)
        {
            if (ex == null)
            {
                return false;
            }

            var exType = ex.GetType();
            if (exType == typeof(HttpRequestException) ||
                exType == typeof(WebException) ||
                exType == typeof(SocketException) ||
                exType == typeof(TimeoutException))
            {
                return true;
            }

            if (exType == typeof(OperationCanceledException) || exType == typeof(IOException))
            {
                return NetworkExceptionHelper.IsNetworkException(ex);
            }

            return false;
        }

        private static void LogHttpException(BotData data, NormalizedHttpRequest request, Exception ex, string transportName)
        {
            var severityColor = IsLikelyNetworkException(ex) ? LogColors.Orange : LogColors.Tomato;
            var proxySummary = data.UseProxy && data.Proxy != null
                ? $"{data.Proxy.Type}://{data.Proxy.Host}:{data.Proxy.Port}"
                : "disabled";

            data.Logger.Log($"HTTP exception [{transportName}]: {ex.PrettyPrint()}", severityColor);
            data.Logger.Log(
                $"HTTP context: method={request.Method.Method}, url={request.Uri}, timeout={FormatRequestTimeout(request.TimeoutMilliseconds)}, redirects={request.RemainingRedirects}, autoRedirect={request.AutoRedirect}, readResponseContent={request.ReadResponseContent}, proxy={proxySummary}",
                LogColors.Orange);
            data.Logger.Log(
                $"HTTP transport settings: connectTimeout={FormatTimeSpan(data.Providers.ProxySettings.ConnectTimeout)}, readWriteTimeout={FormatTimeSpan(data.Providers.ProxySettings.ReadWriteTimeout)}, absoluteUri={request.AbsoluteUriInFirstLine}, allowHttpsToHttpRedirect={request.AllowHttpsToHttpRedirect}",
                LogColors.Orange);

            if (ShouldLogStackTrace(data, ex))
            {
                data.Logger.Log("HTTP exception stack trace:", LogColors.Gray);
                data.Logger.Log(ex.ToString(), LogColors.Gray);
            }
        }

        private static bool ShouldLogStackTrace(BotData data, Exception ex)
            => data.ConfigSettings.GeneralSettings.VerboseMode
            || ex is ArgumentException
            || ex is InvalidOperationException
            || ex is NotSupportedException;

        private static string FormatRequestTimeout(int timeoutMilliseconds)
            => timeoutMilliseconds == -1 ? "disabled" : $"{timeoutMilliseconds}ms";

        private static string FormatTimeSpan(TimeSpan timeout)
            => timeout == Timeout.InfiniteTimeSpan ? "disabled" : $"{timeout.TotalMilliseconds:0}ms";

        protected static NormalizedHttpResponse CreateResponseSnapshot(
            Uri address,
            int statusCode,
            Dictionary<string, List<string>> headers,
            byte[] body)
            => new()
            {
                Address = address,
                StatusCode = statusCode,
                Headers = headers,
                RawBody = body ?? Array.Empty<byte>()
            };

        protected static Dictionary<string, List<string>> NormalizeHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in headers)
            {
                if (!normalized.TryGetValue(header.Key, out var values))
                {
                    values = new List<string>();
                    normalized[header.Key] = values;
                }

                foreach (var value in header.Value ?? Array.Empty<string>())
                {
                    values.Add(value);
                }
            }

            return normalized;
        }

        protected static Dictionary<string, List<string>> NormalizeHeaders(IEnumerable<KeyValuePair<string, string>> headers)
        {
            var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in headers)
            {
                if (!normalized.TryGetValue(header.Key, out var values))
                {
                    values = new List<string>();
                    normalized[header.Key] = values;
                }

                values.Add(header.Value);
            }

            return normalized;
        }

        private static NormalizedHttpRequest CreateBaseNormalizedRequest(BotData data, Options.HttpRequestOptions options)
            => new()
            {
                Uri = new Uri(options.Url),
                Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                Version = Version.Parse(options.HttpVersion),
                Headers = PrepareHeadersAndCookies(data, options),
                Cookies = data.COOKIES,
                AutoRedirect = options.AutoRedirect,
                RemainingRedirects = options.MaxNumberOfRedirects,
                TimeoutMilliseconds = options.TimeoutMilliseconds,
                AbsoluteUriInFirstLine = options.AbsoluteUriInFirstLine,
                ReadResponseContent = options.ReadResponseContent,
                DecodeHtml = options.DecodeHtml,
                DisableCookieParsing = options.DisableCookieParsing,
                DisableHeaderParsing = options.DisableHeaderParsing,
                CodePagesEncoding = options.CodePagesEncoding,
                AllowHttpsToHttpRedirect = options.AllowHttpsToHttpRedirect
            };

        private static void ValidateRequest(NormalizedHttpRequest request)
        {
            if (request.TimeoutMilliseconds == 0 || request.TimeoutMilliseconds < -1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.TimeoutMilliseconds),
                    request.TimeoutMilliseconds,
                    "TimeoutMilliseconds must be greater than 0, or -1 to disable the timeout.");
            }

            if (request.RemainingRedirects < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.RemainingRedirects),
                    request.RemainingRedirects,
                    "MaxNumberOfRedirects must be zero or greater.");
            }
        }

        private static bool TryCreateRedirectRequest(
            NormalizedHttpRequest request,
            NormalizedHttpResponse response,
            out NormalizedHttpRequest redirectRequest)
        {
            redirectRequest = null;

            if (!request.AutoRedirect || request.RemainingRedirects <= 0)
            {
                return false;
            }

            if (response.StatusCode is < 300 or >= 400)
            {
                return false;
            }

            if (!TryGetSingleHeaderValue(response.Headers, "Location", out var locationValue) ||
                string.IsNullOrWhiteSpace(locationValue))
            {
                return false;
            }

            var targetUri = Uri.TryCreate(locationValue, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri
                : new Uri(request.Uri, locationValue);

            if (!request.AllowHttpsToHttpRedirect &&
                request.Uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                targetUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var redirectHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (TryGetHeaderValue(request.Headers, "User-Agent", out var userAgent))
            {
                redirectHeaders["User-Agent"] = userAgent;
            }

            if (!string.IsNullOrEmpty(request.RedirectAuthorization) &&
                request.Uri.Host.Equals(targetUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                redirectHeaders["Authorization"] = request.RedirectAuthorization;
            }

            redirectRequest = request.CreateRedirect(targetUri, redirectHeaders);
            return true;
        }

        private static void LogHttpRequestData(BotData data, NormalizedHttpRequest request)
        {
            using var writer = new StringWriter();
            writer.WriteLine($"{request.Method.Method} {request.Uri.PathAndQuery} HTTP/{request.Version.Major}.{request.Version.Minor}");

            if (!TryGetHeaderValue(request.Headers, "Host", out _))
            {
                writer.WriteLine($"Host: {request.Uri.Host}");
            }

            foreach (var header in request.Headers)
            {
                writer.WriteLine($"{header.Key}: {header.Value}");
            }

            var cookies = request.Cookies.Where(static c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}");
            if (cookies.Any())
            {
                writer.WriteLine($"Cookie: {string.Join("; ", cookies)}");
            }

            if (!string.IsNullOrEmpty(request.LoggedContent))
            {
                if (!string.IsNullOrWhiteSpace(request.ContentType))
                {
                    writer.WriteLine($"Content-Type: {request.ContentType}");
                }

                if (!string.IsNullOrWhiteSpace(request.ContentLengthDisplay))
                {
                    writer.WriteLine($"Content-Length: {request.ContentLengthDisplay}");
                }

                writer.WriteLine();
                writer.WriteLine(request.LoggedContent);
            }

            data.Logger.Log(writer.ToString(), LogColors.Azure);
        }

        private static void ApplyResponseData(BotData data, NormalizedHttpResponse response, NormalizedHttpRequest request)
        {
            data.ADDRESS = response.Address.AbsoluteUri;
            data.Logger.Log($"Address: {data.ADDRESS}", LogColors.DodgerBlue);

            data.RESPONSECODE = response.StatusCode;
            data.Logger.Log($"Response code: {data.RESPONSECODE}", LogColors.Citrine);

            data.RAWSOURCE = request.ReadResponseContent && response.StatusCode is < 300 or >= 400
                ? response.RawBody ?? Array.Empty<byte>()
                : Array.Empty<byte>();

            var parsedCookies = ParseResponseCookies(response.Headers);

            if (!request.DisableHeaderParsing)
            {
                data.HEADERS = BuildLoggedHeaders(response.Headers, parsedCookies);

                if (!data.HEADERS.ContainsKey("Content-Length"))
                {
                    data.HEADERS["Content-Length"] = data.RAWSOURCE.Length.ToString();
                }

                data.Logger.Log("Received Headers:", LogColors.MediumPurple);
                data.Logger.Log(data.HEADERS.Select(h => $"{h.Key}: {h.Value}"), LogColors.Violet);
            }
            else
            {
                data.HEADERS.Clear();
                data.HEADERS["Content-Length"] = data.RAWSOURCE.Length.ToString();
                data.Logger.Log("Header Parsing Skipped", LogColors.Orange);
            }

            if (!request.DisableCookieParsing)
            {
                foreach (var cookie in parsedCookies)
                {
                    data.COOKIES[cookie.Name] = cookie.Value;
                }

                data.Logger.Log("Received Cookies:", LogColors.MikadoYellow);
                data.Logger.Log(data.COOKIES.Select(h => $"{h.Key}: {h.Value}"), LogColors.Khaki);
            }
            else
            {
                data.Logger.Log("Cookie Parsing Skipped", LogColors.Orange);
            }

            DecodeResponseBody(data, request);

            if (!string.IsNullOrWhiteSpace(request.CodePagesEncoding))
            {
                data.SOURCE = CodePagesEncodingProvider.Instance
                    .GetEncoding(request.CodePagesEncoding)
                    .GetString(data.RAWSOURCE);
            }
            else
            {
                data.SOURCE = Encoding.UTF8.GetString(data.RAWSOURCE);
            }

            if (request.DecodeHtml)
            {
                data.SOURCE = WebUtility.HtmlDecode(data.SOURCE);
            }

            if (response.StatusCode is < 300 or >= 400)
            {
                data.Logger.Log("Received Payload:", LogColors.ForestGreen);
                data.Logger.Log(data.SOURCE, LogColors.GreenYellow, true);
            }
        }

        private static Dictionary<string, string> BuildLoggedHeaders(
            Dictionary<string, List<string>> headers,
            List<ParsedCookie> parsedCookies)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in headers)
            {
                if (cookieHeaderNames.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                {
                    result[header.Key] = parsedCookies.Count > 0
                        ? string.Join(", ", parsedCookies.Select(c => $"{c.Name}={c.RawValue}"))
                        : string.Join(", ", header.Value);
                    continue;
                }

                var separator = commaHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase) ? ", " : " ";
                result[header.Key] = string.Join(separator, header.Value);
            }

            return result;
        }

        private static List<ParsedCookie> ParseResponseCookies(Dictionary<string, List<string>> headers)
        {
            var cookies = new List<ParsedCookie>();

            foreach (var headerName in cookieHeaderNames)
            {
                if (!headers.TryGetValue(headerName, out var values))
                {
                    continue;
                }

                foreach (var combinedValue in values)
                {
                    foreach (var cookieHeader in SplitSetCookieHeaders(combinedValue))
                    {
                        if (TryParseCookie(cookieHeader, out var cookie))
                        {
                            cookies.Add(cookie);
                        }
                    }
                }
            }

            return cookies;
        }

        private static void DecodeResponseBody(BotData data, NormalizedHttpRequest request)
        {
            if (data.HEADERS.TryGetValue("Content-Encoding", out var contentEncoding) &&
                contentEncoding.Contains("br", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var inputStream = new MemoryStream(data.RAWSOURCE);
                    using var outputStream = new MemoryStream();
                    using var brotli = new BrotliStream(inputStream, CompressionMode.Decompress, false);
                    brotli.CopyTo(outputStream);
                    data.RAWSOURCE = outputStream.ToArray();
                }
                catch
                {
                    data.Logger.Log("[WARNING] Tried to decompress brotli but failed", LogColors.DarkOrange);
                }
            }

            if (data.RAWSOURCE.Length > 1 && data.RAWSOURCE[0] == 0x1F && data.RAWSOURCE[1] == 0x8B)
            {
                try
                {
                    data.RAWSOURCE = GZip.Unzip(data.RAWSOURCE);
                }
                catch
                {
                    data.Logger.Log("[WARNING] Tried to decompress gzip but failed", LogColors.DarkOrange);
                }
            }
        }

        protected static HttpContent? CreateHttpContent(BotData data, NormalizedHttpRequest request, out FileStream? fileStream)
        {
            fileStream = null;

            if (request.MultipartContents != null)
            {
                var multipartContent = new MultipartFormDataContent(request.Boundary);
                multipartContent.Headers.ContentType.Parameters.First(o => o.Name == "boundary").Value = request.Boundary;

                foreach (var content in request.MultipartContents)
                {
                    switch (content)
                    {
                        case StringHttpContent stringContent:
                            multipartContent.Add(new StringContent(stringContent.Data, Encoding.UTF8, stringContent.ContentType), stringContent.Name);
                            break;

                        case RawHttpContent rawContent:
                            var byteContent = new ByteArrayContent(rawContent.Data);
                            byteContent.Headers.ContentType = new MediaTypeHeaderValue(rawContent.ContentType);
                            multipartContent.Add(byteContent, rawContent.Name);
                            break;

                        case FileHttpContent fileContent:
                            lock (FileLocker.GetHandle(fileContent.FileName))
                            {
                                if (data.Providers.Security.RestrictBlocksToCWD)
                                {
                                    FileUtils.ThrowIfNotInCWD(fileContent.FileName);
                                }

                                fileStream = new FileStream(fileContent.FileName, FileMode.Open);
                                multipartContent.Add(
                                    CreateFileContent(fileStream, fileContent.Name, Path.GetFileName(fileContent.FileName), fileContent.ContentType),
                                    fileContent.Name);
                            }
                            break;
                    }
                }

                return multipartContent;
            }

            if (request.RawBody != null)
            {
                var rawContent = new ByteArrayContent(request.RawBody);
                if (!string.IsNullOrWhiteSpace(request.ContentType))
                {
                    rawContent.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
                }

                return rawContent;
            }

            if (request.StringBody == null && string.IsNullOrWhiteSpace(request.ContentType))
            {
                return null;
            }

            var stringContentValue = request.StringBody?.Unescape() ?? string.Empty;
            var textContent = new StringContent(stringContentValue);
            if (!string.IsNullOrWhiteSpace(request.ContentType))
            {
                textContent.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
            }

            return textContent;
        }

        protected static Dictionary<string, string> CopyHeaders(NormalizedHttpRequest request)
            => request.Headers.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        protected static Dictionary<string, string> CopyCookies(NormalizedHttpRequest request)
            => request.Cookies.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        private static bool TryParseCookie(string cookieHeader, out ParsedCookie cookie)
        {
            cookie = default;

            if (string.IsNullOrWhiteSpace(cookieHeader))
            {
                return false;
            }

            var separatorPos = cookieHeader.IndexOf('=');
            if (separatorPos <= 0)
            {
                return false;
            }

            var name = cookieHeader.AsSpan(0, separatorPos).ToString().Trim();
            var endCookiePos = cookieHeader.IndexOf(';', separatorPos);
            var rawValue = endCookiePos == -1
                ? cookieHeader.AsSpan(separatorPos + 1).ToString().Trim()
                : cookieHeader.AsSpan(separatorPos + 1, endCookiePos - separatorPos - 1).ToString().Trim();

            var value = rawValue;
            if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
            {
                value = value[1..^1];
            }

            cookie = new ParsedCookie(name, value, rawValue);
            return true;
        }

        private static string[] SplitSetCookieHeaders(string combinedHeader)
        {
            if (string.IsNullOrWhiteSpace(combinedHeader))
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            var inQuotes = false;
            var startIndex = 0;

            for (var i = 0; i < combinedHeader.Length; i++)
            {
                var c = combinedHeader[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (c != ',' || inQuotes)
                {
                    continue;
                }

                var nextIndex = i + 1;
                while (nextIndex < combinedHeader.Length && char.IsWhiteSpace(combinedHeader[nextIndex]))
                {
                    nextIndex++;
                }

                var tempIndex = nextIndex;
                while (tempIndex < combinedHeader.Length &&
                       !char.IsWhiteSpace(combinedHeader[tempIndex]) &&
                       combinedHeader[tempIndex] != '=')
                {
                    tempIndex++;
                }

                if (tempIndex < combinedHeader.Length && combinedHeader[tempIndex] == '=')
                {
                    var segment = combinedHeader.Substring(startIndex, i - startIndex).Trim();
                    if (segment.Length > 0)
                    {
                        result.Add(segment);
                    }

                    startIndex = i + 1;
                }
            }

            if (startIndex < combinedHeader.Length)
            {
                var segment = combinedHeader.Substring(startIndex).Trim();
                if (segment.Length > 0)
                {
                    result.Add(segment);
                }
            }

            return result.ToArray();
        }

        private static bool TryGetSingleHeaderValue(
            Dictionary<string, List<string>> headers,
            string headerName,
            out string value)
        {
            value = string.Empty;
            if (!headers.TryGetValue(headerName, out var values) || values.Count == 0)
            {
                return false;
            }

            value = values[0];
            return true;
        }

        private static bool TryGetHeaderValue(
            Dictionary<string, string> headers,
            string headerName,
            out string value)
        {
            value = string.Empty;
            var key = headers.Keys.FirstOrDefault(k => k.Equals(headerName, StringComparison.OrdinalIgnoreCase));
            if (key is null)
            {
                return false;
            }

            value = headers[key];
            return true;
        }

        protected static void MergeCookieHeader(IDictionary<string, string> headers, IDictionary<string, string> cookieJar)
        {
            if (headers == null || headers.Count == 0)
            {
                return;
            }

            var cookieKeys = headers.Keys
                .Where(k => k.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
                            k.Equals("cookies", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var cookieKey in cookieKeys)
            {
                var raw = headers[cookieKey];
                if (string.IsNullOrWhiteSpace(raw))
                {
                    headers.Remove(cookieKey);
                    continue;
                }

                foreach (var part in raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
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
                    var value = trimmed[(idx + 1)..].Trim();
                    if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
                    {
                        value = value[1..^1];
                    }

                    if (!string.IsNullOrEmpty(value))
                    {
                        cookieJar[name] = value;
                    }
                }

                headers.Remove(cookieKey);
            }
        }

        protected sealed class NormalizedHttpRequest
        {
            public required Uri Uri { get; init; }
            public required System.Net.Http.HttpMethod Method { get; init; }
            public required Version Version { get; init; }
            public required Dictionary<string, string> Headers { get; init; }
            public required IDictionary<string, string> Cookies { get; init; }
            public string? StringBody { get; set; }
            public byte[]? RawBody { get; set; }
            public List<MyHttpContent>? MultipartContents { get; set; }
            public string? Boundary { get; set; }
            public string? LoggedContent { get; set; }
            public string? ContentType { get; set; }
            public string? ContentLengthDisplay { get; set; }
            public string? RedirectAuthorization { get; set; }
            public bool AutoRedirect { get; init; }
            public int RemainingRedirects { get; init; }
            public int TimeoutMilliseconds { get; init; }
            public bool AbsoluteUriInFirstLine { get; init; }
            public bool ReadResponseContent { get; init; }
            public bool DecodeHtml { get; init; }
            public bool DisableCookieParsing { get; init; }
            public bool DisableHeaderParsing { get; init; }
            public string CodePagesEncoding { get; init; } = string.Empty;
            public bool AllowHttpsToHttpRedirect { get; init; }

            public NormalizedHttpRequest CreateRedirect(Uri targetUri, Dictionary<string, string> headers)
                => new()
                {
                    Uri = targetUri,
                    Method = System.Net.Http.HttpMethod.Get,
                    Version = Version,
                    Headers = headers,
                    Cookies = Cookies,
                    RedirectAuthorization = RedirectAuthorization,
                    AutoRedirect = AutoRedirect,
                    RemainingRedirects = RemainingRedirects - 1,
                    TimeoutMilliseconds = TimeoutMilliseconds,
                    AbsoluteUriInFirstLine = AbsoluteUriInFirstLine,
                    ReadResponseContent = ReadResponseContent,
                    DecodeHtml = DecodeHtml,
                    DisableCookieParsing = DisableCookieParsing,
                    DisableHeaderParsing = DisableHeaderParsing,
                    CodePagesEncoding = CodePagesEncoding,
                    AllowHttpsToHttpRedirect = AllowHttpsToHttpRedirect
                };
        }

        protected sealed class NormalizedHttpResponse
        {
            public required Uri Address { get; init; }
            public required int StatusCode { get; init; }
            public required Dictionary<string, List<string>> Headers { get; init; }
            public byte[] RawBody { get; init; } = Array.Empty<byte>();
        }

        private readonly record struct ParsedCookie(string Name, string Value, string RawValue);
    }
}
