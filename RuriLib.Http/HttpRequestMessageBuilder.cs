using System.Text;
using System.Net.Http;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using RuriLib.Http.Extensions;
using System;
using System.Buffers;

namespace RuriLib.Http
{
    static internal class HttpRequestMessageBuilder
    {
        private static readonly string newLine = "\r\n";
        private static readonly byte[] CRLF_Bytes = Encoding.ASCII.GetBytes("\r\n");
        private static readonly byte[] Space_Bytes = Encoding.ASCII.GetBytes(" ");
        private static readonly byte[] ColonSpace_Bytes = Encoding.ASCII.GetBytes(": ");
        private static readonly string[] commaHeaders = new[] { "Accept", "Accept-Encoding" };

        // Builds the first line, for example
        // GET /resource HTTP/1.1
        public static string BuildFirstLine(HttpRequestMessage request)
        {
            if (request.Version >= new Version(2, 0))
                throw new Exception($"HTTP/{request.Version.Major}.{request.Version.Minor} not supported yet");

            return $"{request.Method.Method} {request.RequestUri.PathAndQuery} HTTP/{request.Version}{newLine}";
        }

        // Builds the first line, for example
        // GET /resource HTTP/1.1
        public static void BuildFirstLine(IBufferWriter<byte> writer, HttpRequestMessage request)
        {
            if (request.Version >= new Version(2, 0))
                throw new Exception($"HTTP/{request.Version.Major}.{request.Version.Minor} not supported yet");

            writer.Write(Encoding.ASCII.GetBytes(request.Method.Method));
            writer.Write(Space_Bytes);
            writer.Write(Encoding.ASCII.GetBytes(request.RequestUri.PathAndQuery));
            writer.Write(Space_Bytes);
            writer.Write(Encoding.ASCII.GetBytes($"HTTP/{request.Version}"));
            writer.Write(CRLF_Bytes);
        }

        // Builds the headers, for example
        // Host: example.com
        // Connection: Close
        public static string BuildHeaders(HttpRequestMessage request, CookieContainer cookies = null)
        {
            // NOTE: Do not use AppendLine because it appends \n instead of \r\n
            // on Unix-like systems.
            var sb = new StringBuilder();
            var headers = new List<KeyValuePair<string, string>>();

            // Add the Host header if not already provided
            if (string.IsNullOrEmpty(request.Headers.Host))
            {
                headers.Add("Host", request.RequestUri.Host);
            }

            // Add the Connection: Close header if none is present
            if (request.Headers.Connection.Count == 0)
            {
                headers.Add("Connection", "Close");
            }

            // Add the non-content headers
            foreach (var header in request.Headers)
            {
                headers.Add(header.Key, GetHeaderValue(header));
            }

            // Add the Cookie header
            if (cookies != null)
            {
                var cookiesCollection = cookies.GetCookies(request.RequestUri);
                if (cookiesCollection.Count > 0)
                {
                    var cookieBuilder = new StringBuilder();

                    foreach (var cookie in cookiesCollection)
                    {
                        cookieBuilder
                            .Append(cookie)
                            .Append("; ");
                    }

                    // Remove the last ; and space if not empty
                    if (cookieBuilder.Length > 2)
                    {
                        cookieBuilder.Remove(cookieBuilder.Length - 2, 2);
                    }

                    headers.Add("Cookie", cookieBuilder.ToString());
                }
            }

            // Add the content headers
            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    headers.Add(header.Key, GetHeaderValue(header));
                }

                // Add the Content-Length header if not already present
                if (!headers.Any(h => h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)))
                {
                    var contentLength = request.Content.Headers.ContentLength;

                    if (contentLength.HasValue && contentLength.Value > 0)
                    {
                        headers.Add("Content-Length", contentLength.Value.ToString());
                    }
                }
            }

            // Write all non-empty headers to the StringBuilder
            foreach (var header in headers.Where(h => !string.IsNullOrEmpty(h.Value)))
            {
                sb
                    .Append(header.Key)
                    .Append(": ")
                    .Append(header.Value)
                    .Append(newLine);
            }

            // Write the final blank line after all headers
            sb.Append(newLine);

            return sb.ToString();
        }

        // Builds the headers, for example
        // Host: example.com
        // Connection: Close
        public static void BuildHeaders(IBufferWriter<byte> writer, HttpRequestMessage request, CookieContainer cookies = null)
        {
            var headers = new List<KeyValuePair<string, string>>();

            // Add the Host header if not already provided
            if (string.IsNullOrEmpty(request.Headers.Host))
            {
                headers.Add("Host", request.RequestUri.Host);
            }

            // Add the Connection: Close header if none is present
            if (request.Headers.Connection.Count == 0)
            {
                headers.Add("Connection", "Close");
            }

            // Add the non-content headers
            foreach (var header in request.Headers)
            {
                headers.Add(header.Key, GetHeaderValue(header));
            }

            // Add the Cookie header
            if (cookies != null)
            {
                var cookiesCollection = cookies.GetCookies(request.RequestUri);
                if (cookiesCollection.Count > 0)
                {
                    var cookieBuilder = new StringBuilder();

                    foreach (var cookie in cookiesCollection)
                    {
                        cookieBuilder
                            .Append(cookie)
                            .Append("; ");
                    }

                    // Remove the last ; and space if not empty
                    if (cookieBuilder.Length > 2)
                    {
                        cookieBuilder.Remove(cookieBuilder.Length - 2, 2);
                    }

                    headers.Add("Cookie", cookieBuilder.ToString());
                }
            }

            // Add the content headers
            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    headers.Add(header.Key, GetHeaderValue(header));
                }

                // Add the Content-Length header if not already present
                if (!headers.Any(h => h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)))
                {
                    var contentLength = request.Content.Headers.ContentLength;

                    if (contentLength.HasValue && contentLength.Value > 0)
                    {
                        headers.Add("Content-Length", contentLength.Value.ToString());
                    }
                }
            }

            // Write all non-empty headers to the IBufferWriter<byte>
            foreach (var header in headers.Where(h => !string.IsNullOrEmpty(h.Value)))
            {
                writer.Write(Encoding.ASCII.GetBytes(header.Key));
                writer.Write(ColonSpace_Bytes);
                writer.Write(Encoding.ASCII.GetBytes(header.Value));
                writer.Write(CRLF_Bytes);
            }

            // Write the final blank line after all headers
            writer.Write(CRLF_Bytes);
        }

        private static string GetHeaderValue(KeyValuePair<string, IEnumerable<string>> header)
        {
            var values = header.Value.ToArray();

            return values.Length switch
            {
                0 => string.Empty,
                1 => values[0],
                _ => string.Join(commaHeaders.Contains(header.Key) ? ", " : " ", values)
            };
        }
    }
}
