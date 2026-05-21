using RuriLib.Functions.Files;
using RuriLib.Helpers;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;

namespace RuriLib.Functions.Http
{
    internal static class HttpResponseMapper
    {
        private static readonly string[] CommaHeaders = new[] { "Accept", "Accept-Encoding" };
        private static readonly string[] CookieHeaderNames = new[] { "Set-Cookie", "Set-Cookie2" };

        public static void Apply(BotData data, NormalizedHttpResponse response, NormalizedHttpRequest request)
        {
            data.ADDRESS = response.Address.AbsoluteUri;
            data.Logger.Log($"Address: {data.ADDRESS}", LogColors.DodgerBlue);

            data.RESPONSECODE = response.StatusCode;
            data.Logger.Log($"Response code: {data.RESPONSECODE}", LogColors.Citrine);

            data.RAWSOURCE = request.ReadResponseContent
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
                data.Logger.Log(data.HEADERS.Select(h => HttpPipelineLogger.FormatHeaderForLog(h.Key, h.Value)), LogColors.Violet);
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
                data.Logger.Log(data.COOKIES.Select(h => HttpPipelineLogger.FormatCookieForLog(h.Key)), LogColors.Khaki);
            }
            else
            {
                data.Logger.Log("Cookie Parsing Skipped", LogColors.Orange);
            }

            DecodeResponseBody(data);

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

            if (request.ReadResponseContent && response.RawBody is { Length: > 0 })
            {
                data.Logger.Log("Received Payload:", LogColors.ForestGreen);
                data.Logger.Log(HttpPipelineLogger.FormatPayloadForLog(data.SOURCE), LogColors.GreenYellow, true);
            }
        }

        private static Dictionary<string, string> BuildLoggedHeaders(
            Dictionary<string, List<string>> headers,
            List<ParsedCookie> parsedCookies)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in headers)
            {
                if (CookieHeaderNames.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                {
                    result[header.Key] = parsedCookies.Count > 0
                        ? string.Join(", ", parsedCookies.Select(c => $"{c.Name}={c.RawValue}"))
                        : string.Join(", ", header.Value);
                    continue;
                }

                var separator = CommaHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase) ? ", " : " ";
                result[header.Key] = string.Join(separator, header.Value);
            }

            return result;
        }

        private static List<ParsedCookie> ParseResponseCookies(Dictionary<string, List<string>> headers)
        {
            var cookies = new List<ParsedCookie>();

            foreach (var headerName in CookieHeaderNames)
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

        private static void DecodeResponseBody(BotData data)
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
    }
}
