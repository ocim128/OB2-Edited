using RuriLib.Extensions;
using RuriLib.Functions.Http.Options;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Text;

namespace RuriLib.Functions.Http
{
    internal static class HttpRequestNormalizer
    {
        public static TlsCipherSuite[] ParseCipherSuites(List<string> cipherSuites)
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

        public static HttpOptions GetClientOptions(BotData data, HttpRequestOptions options) => new()
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

        public static NormalizedHttpRequest Create(BotData data, StandardHttpRequestOptions options)
        {
            var request = CreateBaseRequest(data, options);
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

        public static NormalizedHttpRequest Create(BotData data, RawHttpRequestOptions options)
        {
            var request = CreateBaseRequest(data, options);
            request.RawBody = options.Content ?? Array.Empty<byte>();
            request.LoggedContent = Convert.ToBase64String(request.RawBody);
            request.ContentType = options.ContentType;
            request.ContentLengthDisplay = request.RawBody.Length.ToString();
            return request;
        }

        public static NormalizedHttpRequest Create(BotData data, BasicAuthHttpRequestOptions options)
        {
            var request = CreateBaseRequest(data, options);
            request.RedirectAuthorization = "Basic " + Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
            request.Headers["Authorization"] = request.RedirectAuthorization;
            return request;
        }

        public static NormalizedHttpRequest Create(BotData data, MultipartHttpRequestOptions options)
        {
            var request = CreateBaseRequest(data, options);
            request.Boundary = string.IsNullOrWhiteSpace(options.Boundary)
                ? GenerateMultipartBoundary()
                : options.Boundary;
            request.MultipartContents = options.Contents;
            request.ContentType = $"multipart/form-data; boundary=\"{request.Boundary}\"";
            request.ContentLengthDisplay = "(not calculated)";
            request.LoggedContent = SerializeMultipart(request.Boundary, options.Contents);
            return request;
        }

        public static void Validate(NormalizedHttpRequest request)
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

        private static NormalizedHttpRequest CreateBaseRequest(BotData data, HttpRequestOptions options)
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

        private static Dictionary<string, string> PrepareHeadersAndCookies(BotData data, HttpRequestOptions options)
        {
            foreach (var cookie in options.CustomCookies)
            {
                data.COOKIES[cookie.Key] = cookie.Value;
            }

            var headers = NormalizeSingleValueHeaders(options.CustomHeaders);
            MergeCookieHeader(headers, data.COOKIES);
            return headers;
        }

        internal static Dictionary<string, string> NormalizeSingleValueHeaders(
            IEnumerable<KeyValuePair<string, string>>? headers)
        {
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (headers == null)
            {
                return normalized;
            }

            foreach (var header in headers)
            {
                normalized[header.Key] = header.Value;
            }

            return normalized;
        }

        private static string GenerateMultipartBoundary()
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

        private static string SerializeMultipart(string boundary, List<MyHttpContent> contents)
        {
            using var writer = new System.IO.StringWriter();

            foreach (var content in contents)
            {
                writer.WriteLine(boundary);

                switch (content)
                {
                    case StringHttpContent stringContent:
                        writer.WriteLine($"Content-Disposition: form-data; name={stringContent.Name}");
                        writer.WriteLine($"Content-Type: {GetMediaHeaderString(stringContent.ContentType)}");
                        writer.WriteLine();
                        writer.WriteLine(stringContent.Data);
                        break;

                    case RawHttpContent rawContent:
                        writer.WriteLine($"Content-Disposition: form-data; name={rawContent.Name}");
                        writer.WriteLine($"Content-Type: {GetMediaHeaderString(rawContent.ContentType)}");
                        writer.WriteLine();
                        writer.WriteLine(Encoding.UTF8.GetString(rawContent.Data));
                        break;

                    case FileHttpContent fileContent:
                        writer.WriteLine($"Content-Disposition: form-data; name=\"{fileContent.Name}\"; filename=\"{System.IO.Path.GetFileName(fileContent.FileName)}\"");
                        writer.WriteLine($"Content-Type: {GetMediaHeaderString(fileContent.ContentType)}");
                        writer.WriteLine();
                        writer.WriteLine("[FILE CONTENTS NOT LOGGED]");
                        break;
                }
            }

            writer.WriteLine(boundary);
            return writer.ToString();
        }

        private static string GetMediaHeaderString(string contentType)
            => new System.Net.Http.Headers.MediaTypeHeaderValue(contentType).ToString();

        private static void MergeCookieHeader(IDictionary<string, string> headers, IDictionary<string, string> cookieJar)
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
    }
}
