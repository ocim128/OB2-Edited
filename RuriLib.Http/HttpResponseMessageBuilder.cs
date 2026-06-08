using System;
using System.IO;
using System.Net;
using System.Text;
using System.Net.Http;
using System.Threading;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using RuriLib.Http.Helpers;
using System.IO.Pipelines;
using System.Buffers;

namespace RuriLib.Http;

/// <summary>
/// Builds HTTP response messages from raw HTTP data with optimized parsing and memory usage.
/// </summary>
internal sealed class HttpResponseMessageBuilder
{
    private const string NewLine = "\r\n";
    private static readonly byte[] CRLF = Encoding.UTF8.GetBytes(NewLine);

    private readonly CookieContainer _cookies;
    private readonly Uri _uri;

    private PipeReader _reader;
    private HttpResponseMessage _response;
    private Dictionary<string, List<string>> _contentHeaders;
    private long _contentLength = -1;

    /// <summary>
    /// Gets or sets the timeout for receive operations.
    /// </summary>
    public TimeSpan ReceiveTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Initializes a new instance of the HttpResponseMessageBuilder class.
    /// </summary>
    /// <param name="cookies">The cookie container for handling cookies.</param>
    /// <param name="uri">The URI of the request.</param>
    public HttpResponseMessageBuilder(CookieContainer cookies = null, Uri uri = null)
    {
        _cookies = cookies;
        _uri = uri;
    }

    /// <summary>
    /// Builds an HTTP response message from the provided pipe reader.
    /// </summary>
    /// <param name="request">The HTTP request message.</param>
    /// <param name="reader">The pipe reader containing the response data.</param>
    /// <param name="readResponseContent">Whether to read the response content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The built HTTP response message.</returns>
    public async Task<HttpResponseMessage> BuildResponseAsync(
        HttpRequestMessage request,
        PipeReader reader,
        bool readResponseContent = true,
        CancellationToken cancellationToken = default)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _response = new HttpResponseMessage();
        _contentHeaders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            _response.RequestMessage = request;

            await ParseStatusLineAsync(cancellationToken).ConfigureAwait(false);
            await ParseHeadersAsync(cancellationToken).ConfigureAwait(false);
            await ParseContentAsync(request, readResponseContent, cancellationToken).ConfigureAwait(false);

            return _response;
        }
        catch
        {
            _response?.Dispose();
            throw;
        }
        finally
        {
            if (_reader != null)
            {
                await _reader.CompleteAsync().ConfigureAwait(false);
                _reader = null;
            }
        }
    }

    /// <summary>
    /// Parses the HTTP status line (e.g., "HTTP/1.1 200 OK").
    /// </summary>
    private async Task ParseStatusLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            if (TryParseStatusLine(ref buffer))
            {
                _reader.AdvanceTo(buffer.Start);
                return;
            }

            if (result.IsCompleted)
            {
                throw new InvalidOperationException("Incomplete HTTP response status line");
            }

            _reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    /// <summary>
    /// Attempts to parse the status line from the buffer.
    /// </summary>
    private bool TryParseStatusLine(ref ReadOnlySequence<byte> buffer)
    {
        var reader = new SequenceReader<byte>(buffer);

        if (!reader.TryReadTo(out ReadOnlySpan<byte> line, CRLF, true))
        {
            return false;
        }

        var statusLine = Encoding.ASCII.GetString(line);
        var parts = statusLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 ||
            !parts[0].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(parts[1], out var statusCode))
        {
            throw new FormatException($"Invalid first line of the HTTP response: {statusLine}");
        }

        _response.Version = Version.Parse(parts[0][5..]);
        _response.StatusCode = (HttpStatusCode)statusCode;
        _response.ReasonPhrase = parts.Length == 3 ? parts[2] : null;
        buffer = buffer.Slice(reader.Position);
        return true;
    }

    /// <summary>
    /// Parses HTTP headers from the response.
    /// </summary>
    private async Task ParseHeadersAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            if (TryParseHeaders(ref buffer))
            {
                _reader.AdvanceTo(buffer.Start);
                return;
            }

            if (result.IsCompleted)
            {
                throw new InvalidOperationException("Incomplete HTTP headers");
            }

            _reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    /// <summary>
    /// Attempts to parse headers from the buffer.
    /// </summary>
    private bool TryParseHeaders(ref ReadOnlySequence<byte> buffer)
    {
        var reader = new SequenceReader<byte>(buffer);

        while (reader.TryReadTo(out ReadOnlySpan<byte> line, CRLF, true))
        {
            if (line.Length == 0) // Empty line indicates end of headers
            {
                buffer = buffer.Slice(reader.Position);
                return true;
            }

            ProcessHeaderLine(line);
        }

        buffer = buffer.Slice(reader.Position);
        return false;
    }

    /// <summary>
    /// Processes a single header line.
    /// </summary>
    private void ProcessHeaderLine(ReadOnlySpan<byte> headerLine)
    {
        if (headerLine.Length == 0) return;

        var colonIndex = headerLine.IndexOf((byte)':');
        if (colonIndex <= 0) return;

        var headerName = Encoding.UTF8.GetString(headerLine[..colonIndex]).Trim();
        var headerValue = Encoding.UTF8.GetString(headerLine[(colonIndex + 1)..]).Trim();

        // Handle cookies
        if (headerName.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
            headerName.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase))
        {
            ProcessCookieHeader(headerValue);
        }
        // Handle content headers
        else if (ContentHelper.IsContentHeader(headerName))
        {
            AddContentHeader(headerName, headerValue);
        }
        // Handle regular headers
        else
        {
            _ = _response.Headers.TryAddWithoutValidation(headerName, headerValue);
        }
    }

    /// <summary>
    /// Processes a Set-Cookie header value.
    /// </summary>
    private void ProcessCookieHeader(string cookieValue)
    {
        if (_cookies == null || string.IsNullOrWhiteSpace(cookieValue)) return;

        try
        {
            var cookie = ParseCookie(cookieValue);
            if (cookie != null)
            {
                _cookies.Add(cookie);
            }
        }
        catch
        {
            // Ignore invalid cookies
        }
    }

    /// <summary>
    /// Parses a cookie from a Set-Cookie header value.
    /// </summary>
    private Cookie ParseCookie(string cookieValue)
    {
        var parts = cookieValue.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var nameValue = parts[0].Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
        if (nameValue.Length != 2) return null;

        var name = nameValue[0].Trim();
        var value = nameValue[1].Trim();

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value) ||
            value.Equals("deleted", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new Cookie(name, value, "/", _uri?.Host ?? "localhost");
    }

    /// <summary>
    /// Adds a content header to the collection.
    /// </summary>
    private void AddContentHeader(string name, string value)
    {
        if (!_contentHeaders.TryGetValue(name, out var values))
        {
            values = new List<string>();
            _contentHeaders[name] = values;
        }
        values.Add(value);
    }

    /// <summary>
    /// Parses the response content.
    /// </summary>
    private async Task ParseContentAsync(HttpRequestMessage request, bool readResponseContent, CancellationToken cancellationToken)
    {
        if (ResponseMustNotHaveBody(request))
        {
            _response.Content = new ByteArrayContent(Array.Empty<byte>());
            AddContentHeaders();
            return;
        }

        _contentLength = GetContentLength();

        if (!readResponseContent)
        {
            _response.Content = new ByteArrayContent(Array.Empty<byte>());
            AddContentHeaders();
            return;
        }

        var contentStream = await GetContentStreamAsync(cancellationToken).ConfigureAwait(false);
        _response.Content = new StreamContent(contentStream);
        AddContentHeaders();
    }

    /// <summary>
    /// Adds content headers to the response content.
    /// </summary>
    private void AddContentHeaders()
    {
        foreach (var (key, values) in _contentHeaders)
        {
            _ = _response.Content.Headers.TryAddWithoutValidation(key, values);
        }
    }

    /// <summary>
    /// Gets the appropriate content stream based on response headers.
    /// </summary>
    private async Task<Stream> GetContentStreamAsync(CancellationToken cancellationToken)
    {
        var hasChunkedTransferEncoding = _response.Headers.TryGetValues("Transfer-Encoding", out var transferEncodings) &&
            transferEncodings.Any(value => value.Contains("chunked", StringComparison.OrdinalIgnoreCase));
        var hasContentEncoding = _contentHeaders.ContainsKey("Content-Encoding");

        if (hasChunkedTransferEncoding)
        {
            return hasContentEncoding
                ? await GetChunkedDecompressedStreamAsync(cancellationToken).ConfigureAwait(false)
                : await GetChunkedStreamAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_contentLength >= 0)
        {
            return hasContentEncoding
                ? await GetContentLengthDecompressedStreamAsync(cancellationToken).ConfigureAwait(false)
                : await GetContentLengthStreamAsync(cancellationToken).ConfigureAwait(false);
        }

        return hasContentEncoding
            ? await GetUntilCloseDecompressedStreamAsync(cancellationToken).ConfigureAwait(false)
            : await GetUntilCloseStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads content with specified length.
    /// </summary>
    private async Task<Stream> GetContentLengthStreamAsync(CancellationToken cancellationToken)
    {
        if (_contentLength == 0)
            return new MemoryStream(Array.Empty<byte>());

        var stream = new MemoryStream(_contentLength <= int.MaxValue ? (int)_contentLength : 0);
        long bytesRead = 0;

        while (bytesRead < _contentLength)
        {
            var result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            var toRead = Math.Min(buffer.Length, _contentLength - bytesRead);
            if (toRead > 0)
            {
                foreach (var segment in buffer)
                {
                    var segmentLength = Math.Min(segment.Length, _contentLength - bytesRead);
                    stream.Write(segment.Span[..(int)segmentLength]);
                    bytesRead += segmentLength;

                    if (bytesRead >= _contentLength) break;
                }
            }

            _reader.AdvanceTo(buffer.GetPosition(toRead));

            if (result.IsCompleted && bytesRead < _contentLength)
                throw new InvalidOperationException("Incomplete response content");
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Reads chunked content.
    /// </summary>
    private async Task<Stream> GetChunkedStreamAsync(CancellationToken cancellationToken)
    {
        var decoder = new ChunkedDecoderOptimized();

        while (!decoder.Finished)
        {
            var result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            decoder.Decode(ref buffer);
            _reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted && !decoder.Finished)
                throw new InvalidOperationException("Incomplete chunked content");
        }

        decoder.DecodedStream.Position = 0;
        return decoder.DecodedStream;
    }

    /// <summary>
    /// Reads content until connection close.
    /// </summary>
    private async Task<Stream> GetUntilCloseStreamAsync(CancellationToken cancellationToken)
    {
        var stream = new MemoryStream();

        while (true)
        {
            var result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            if (buffer.Length > 0)
            {
                foreach (var segment in buffer)
                {
                    stream.Write(segment.Span);
                }
            }

            _reader.AdvanceTo(buffer.End);

            if (result.IsCompleted)
                break;
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Gets decompressed content stream based on content encoding.
    /// </summary>
    private async Task<Stream> GetContentLengthDecompressedStreamAsync(CancellationToken cancellationToken)
    {
        var compressedStream = await GetContentLengthStreamAsync(cancellationToken).ConfigureAwait(false);
        return GetDecompressedStream(compressedStream);
    }

    private async Task<Stream> GetChunkedDecompressedStreamAsync(CancellationToken cancellationToken)
    {
        var compressedStream = await GetChunkedStreamAsync(cancellationToken).ConfigureAwait(false);
        return GetDecompressedStream(compressedStream);
    }

    private async Task<Stream> GetUntilCloseDecompressedStreamAsync(CancellationToken cancellationToken)
    {
        var compressedStream = await GetUntilCloseStreamAsync(cancellationToken).ConfigureAwait(false);
        return GetDecompressedStream(compressedStream);
    }

    /// <summary>
    /// Gets the appropriate decompression stream for the content encoding.
    /// </summary>
    private Stream GetDecompressedStream(Stream compressedStream)
    {
        if (!_contentHeaders.TryGetValue("Content-Encoding", out var encodings) || encodings.Count == 0)
            return compressedStream;

        var encoding = encodings[0].ToLowerInvariant();
        return encoding switch
        {
            "gzip" => new GZipStream(compressedStream, CompressionMode.Decompress, false),
            "deflate" => new DeflateStream(compressedStream, CompressionMode.Decompress, false),
            "br" => new BrotliStream(compressedStream, CompressionMode.Decompress, false),
            _ => compressedStream
        };
    }

    /// <summary>
    /// Gets the content length from headers.
    /// </summary>
    private long GetContentLength()
    {
        if (!_contentHeaders.TryGetValue("Content-Length", out var values))
        {
            return -1;
        }

        long? parsedLength = null;
        foreach (var headerValue in values)
        {
            foreach (var part in headerValue.Split(','))
            {
                if (!long.TryParse(part.Trim(), out var candidate) || candidate < 0)
                {
                    return -1;
                }

                if (parsedLength.HasValue && parsedLength.Value != candidate)
                {
                    return -1;
                }

                parsedLength = candidate;
            }
        }

        return parsedLength ?? -1;
    }

    private bool ResponseMustNotHaveBody(HttpRequestMessage request)
    {
        var statusCode = (int)_response.StatusCode;
        return request.Method == HttpMethod.Head ||
               statusCode is >= 100 and < 200 or 204 or 304;
    }

}
