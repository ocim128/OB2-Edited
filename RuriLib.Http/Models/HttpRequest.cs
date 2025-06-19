using RuriLib.Http.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;

namespace RuriLib.Http.Models
{
    /// <summary>
    /// An HTTP request that can be sent using a <see cref="RLHttpClient"/>.
    /// </summary>
    public class HttpRequest : IDisposable
    {
        /// <summary>
        /// Whether to write the absolute URI in the first line of the request instead of
        /// the relative path (e.g. https://example.com/abc instead of /abc)
        /// </summary>
        public bool AbsoluteUriInFirstLine { get; set; } = false;

        /// <summary>
        /// The HTTP version to use.
        /// </summary>
        public Version Version { get; set; } = new(1, 1);

        /// <summary>
        /// The HTTP method to use.
        /// </summary>
        public HttpMethod Method { get; set; } = HttpMethod.Get;

        /// <summary>
        /// The URI of the remote resource.
        /// </summary>
        public Uri Uri { get; set; }

        /// <summary>
        /// The cookies to send inside the Cookie header of this request.
        /// </summary>
        public Dictionary<string, string> Cookies { get; set; } = new();

        /// <summary>
        /// The headers of this request.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new();

        /// <summary>
        /// The content of this request.
        /// </summary>
        public HttpContent Content { get; set; }

        /// <summary>
        /// Writes the raw bytes that will be sent on the network stream.
        /// </summary>
        /// <param name="writer">The buffer writer to write to</param>
        /// <param name="cancellationToken">The token to cancel the operation</param>
        public async Task WriteToAsync(IBufferWriter<byte> writer, CancellationToken cancellationToken = default)
        {
            BuildFirstLine(writer);
            BuildHeaders(writer);

            if (Content != null)
            {
                var contentBytes = await Content.ReadAsByteArrayAsync(cancellationToken);
                writer.Write(contentBytes);
            }
        }

        private static readonly byte[] CRLF = Encoding.ASCII.GetBytes("\r\n");
        private static readonly byte[] Space = Encoding.ASCII.GetBytes(" ");
        private static readonly byte[] ColonSpace = Encoding.ASCII.GetBytes(": ");
        private static readonly byte[] SemicolonSpace = Encoding.ASCII.GetBytes("; ");

        /// <summary>
        /// Safely adds a header to the dictionary.
        /// </summary>
        public void AddHeader(string name, string value)
        {
            // Make sure Host is written properly otherwise it won't get picked up below
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                Headers["Host"] = value;
            }
            else
            {
                Headers[name] = value;
            }
        }

        // Builds the first line, for example
        // GET /resource HTTP/1.1
        private void BuildFirstLine(IBufferWriter<byte> writer)
        {
            if (Version >= new Version(2, 0))
                throw new Exception($"HTTP/{Version.Major}.{Version.Minor} not supported yet");

            writer.Write(Encoding.ASCII.GetBytes(Method.Method));
            writer.Write(Space);
            writer.Write(Encoding.ASCII.GetBytes(AbsoluteUriInFirstLine ? Uri.AbsoluteUri : Uri.PathAndQuery));
            writer.Write(Space);
            writer.Write(Encoding.ASCII.GetBytes($"HTTP/{Version}"));
            writer.Write(CRLF);
        }

        // Builds the headers, for example
        // Host: example.com
        // Connection: Close
        private void BuildHeaders(IBufferWriter<byte> writer)
        {
            var finalHeaders = new List<KeyValuePair<string, string>>();

            // Add the Host header if not already provided
            if (!HeaderExists("Host", out _))
            {
                finalHeaders.Add("Host", Uri.Host);
            }

            // If there is no Connection header, add it
            if (!HeaderExists("Connection", out var connectionHeaderName))
            {
                finalHeaders.Add("Connection", "Close");
            }

            // Add the non-content headers
            foreach (var header in Headers)
            {
                finalHeaders.Add(header);
            }

            // Add the Cookie header if not set manually and container not null
            if (!HeaderExists("Cookie", out _) && Cookies.Any())
            {
                var firstCookie = true;
                foreach (var cookie in Cookies)
                {
                    if (!firstCookie)
                    {
                        writer.Write(SemicolonSpace);
                    }
                    writer.Write(Encoding.ASCII.GetBytes($"{cookie.Key}={cookie.Value}"));
                    firstCookie = false;
                }
                writer.Write(CRLF);
            }

            // Add the content headers
            if (Content != null)
            {
                foreach (var header in Content.Headers)
                {
                    // If it was already set, skip
                    if (!HeaderExists(header.Key, out _))
                    {
                        finalHeaders.Add(header.Key, string.Join(' ', header.Value));
                    }
                }

                // Add the Content-Length header if not already present
                if (!finalHeaders.Any(h => h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)))
                {
                    var contentLength = Content.Headers.ContentLength;

                    if (contentLength.HasValue && contentLength.Value > 0)
                    {
                        finalHeaders.Add("Content-Length", contentLength.Value.ToString());
                    }
                }
            }

            // Write all non-empty headers to the IBufferWriter<byte>
            foreach (var header in finalHeaders.Where(h => !string.IsNullOrEmpty(h.Value)))
            {
                writer.Write(Encoding.ASCII.GetBytes(header.Key));
                writer.Write(ColonSpace);
                writer.Write(Encoding.ASCII.GetBytes(header.Value));
                writer.Write(CRLF);
            }

            // Write the final blank line after all headers
            writer.Write(CRLF);
        }

        /// <summary>
        /// Checks whether a header that matches a given <paramref name="name"/> exists. If it exists,
        /// its original name will be written to <paramref name="actualName"/>.
        /// </summary>
        public bool HeaderExists(string name, out string actualName)
        {
            var key = Headers.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
            actualName = key;
            return key != null;
        }

        /// <inheritdoc/>
        public void Dispose() => Content?.Dispose();
    }
}
