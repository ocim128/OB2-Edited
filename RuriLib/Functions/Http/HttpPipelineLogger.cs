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
        public static void LogRequest(BotData data, NormalizedHttpRequest request)
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

        public static void LogException(BotData data, NormalizedHttpRequest request, Exception ex, string transportName)
        {
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
