using System;
using System.Collections.Generic;

namespace RuriLib.Functions.Http
{
    internal static class HttpRedirectPolicy
    {
        public static bool TryCreateRedirectRequest(
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

        private static bool TryGetHeaderValue(Dictionary<string, string> headers, string headerName, out string value)
        {
            value = string.Empty;
            foreach (var key in headers.Keys)
            {
                if (!key.Equals(headerName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = headers[key];
                return true;
            }

            return false;
        }
    }
}
