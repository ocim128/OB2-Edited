using RuriLib.Extensions;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace RuriLib.Functions.Http
{
    internal static class HttpPipelineLogger
    {
        private const int MaxLoggedPayloadChars = 16 * 1024;
        private const string RedactedValue = "[redacted]";

        public static void LogRequest(BotData data, NormalizedHttpRequest request)
        {
            if (data.Logger?.Enabled != true)
            {
                return;
            }

            using var writer = new StringWriter();
            writer.WriteLine($"{request.Method.Method} {request.Uri.PathAndQuery} HTTP/{request.Version.Major}.{request.Version.Minor}");

            if (!TryGetHeaderValue(request.Headers, "Host", out _))
            {
                writer.WriteLine($"Host: {request.Uri.Host}");
            }

            foreach (var header in request.Headers)
            {
                writer.WriteLine(FormatHeaderForLog(header.Key, header.Value));
            }

            if (request.Cookies.Any(static c => !string.IsNullOrEmpty(c.Value)))
            {
                writer.WriteLine($"Cookie: {RedactedValue}");
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
                writer.WriteLine(FormatPayloadForLog(request.LoggedContent));
            }

            data.Logger.Log(writer.ToString(), LogColors.Azure);
        }

        public static void LogException(BotData data, NormalizedHttpRequest request, Exception ex, string transportName)
        {
            if (data.Logger?.Enabled != true)
            {
                return;
            }

            var severityColor = HttpExceptionClassifier.IsLikelyNetworkException(ex) ? LogColors.Orange : LogColors.Tomato;
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

        internal static string FormatHeaderForLog(string name, string value)
            => IsSensitiveHeaderName(name)
                ? $"{name}: {RedactedValue}"
                : $"{name}: {value}";

        internal static string FormatCookieForLog(string name)
            => $"{name}: {RedactedValue}";

        internal static string FormatPayloadForLog(string payload)
        {
            if (string.IsNullOrEmpty(payload) || payload.Length <= MaxLoggedPayloadChars)
            {
                return payload;
            }

            return $"{payload[..MaxLoggedPayloadChars]}... [TRUNCATED - {payload.Length} total chars]";
        }

        private static bool IsSensitiveHeaderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase)
                || name.Equals("X-Api-Key", StringComparison.OrdinalIgnoreCase)
                || name.Contains("token", StringComparison.OrdinalIgnoreCase)
                || name.Contains("password", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-Key", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetHeaderValue(Dictionary<string, string> headers, string headerName, out string value)
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
    }
}
