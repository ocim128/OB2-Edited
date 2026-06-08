using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace RuriLib.Http.Models
{
    /// <summary>
    /// An HTTP response obtained with a <see cref="RLHttpClient"/>.
    /// </summary>
    public class HttpResponse : IDisposable
    {
        /// <summary>
        /// The request that retrieved this response.
        /// </summary>
        public HttpRequest Request { get; set; }

        /// <summary>
        /// The HTTP version.
        /// </summary>
        public Version Version { get; set; } = new(1, 1);

        /// <summary>
        /// The status code of the response.
        /// </summary>
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// The headers of the response. Each key maps to a list of values to correctly
        /// support multi-value headers (e.g., Set-Cookie, X-Forwarded-For).
        /// </summary>
        public Dictionary<string, List<string>> Headers { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>
        /// The content of the response.
        /// </summary>
        public HttpContent Content { get; set; }

        /// <summary>
        /// Whether the response framing was fully delimited so the underlying
        /// connection can be reused if no Connection: close token was present.
        /// </summary>
        public bool CanReuseConnection { get; set; }

        /// <summary>
        /// Gets the first value for a given header name, or null if not present.
        /// Convenience helper for single-value header access.
        /// </summary>
        public string GetFirstHeader(string name)
        {
            if (Headers.TryGetValue(name, out var values) && values.Count > 0)
                return values[0];
            return null;
        }

        /// <summary>
        /// Gets all values for a given header name joined by ", " (standard HTTP folding).
        /// Returns null if the header is not present.
        /// </summary>
        public string GetJoinedHeader(string name)
        {
            if (Headers.TryGetValue(name, out var values) && values.Count > 0)
                return string.Join(", ", values);
            return null;
        }

        /// <inheritdoc/>
        public void Dispose() => Content?.Dispose();
    }
}
