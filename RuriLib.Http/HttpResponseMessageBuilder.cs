using System;
using System.IO;
using System.Net;
using System.Text;
using System.Net.Http;
using System.Threading;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Collections.Generic;
using RuriLib.Http.Helpers;
using System.IO.Pipelines;
using System.Buffers;

namespace RuriLib.Http;

/// <summary>
/// Builds HTTP response messages from raw HTTP data with optimized parsing and memory usage.
/// </summary>
internal sealed class HttpResponseMessageBuilder : IAsyncDisposable
{
    private const string NewLine = "\r\n";
    private static readonly byte[] CRLF = Encoding.UTF8.GetBytes(NewLine);

    private readonly CookieContainer _cookies;
    private readonly Uri _uri;

    private PipeReader _reader;
    private HttpResponseMessage _response;
    private Dictionary<string, List<string>> _contentHeaders;
    private int _contentLength = -1;

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
            await ParseContentAsync(readResponseContent, cancellationToken).ConfigureAwait(false);

            return _response;
        }
        catch
        {
            _response?.Dispose();
            throw;
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
    private async Task ParseContentAsync(bool readResponseContent, CancellationToken cancellationToken)
    {
        if (_contentHeaders.Count == 0) return;

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
        var hasTransferEncoding = _response.Headers.Contains("Transfer-Encoding");
        var hasContentEncoding = _contentHeaders.ContainsKey("Content-Encoding");

        if (hasTransferEncoding)
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

        var stream = new MemoryStream(_contentLength);
        var bytesRead = 0;

        while (bytesRead < _contentLength)
        {
            var result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            var toRead = Math.Min((int)buffer.Length, _contentLength - bytesRead);
            if (toRead > 0)
            {
                foreach (var segment in buffer)
                {
                    var segmentLength = Math.Min(segment.Length, _contentLength - bytesRead);
                    await stream.WriteAsync(segment.Slice(0, segmentLength), cancellationToken).ConfigureAwait(false);
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
                    await stream.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
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
    private int GetContentLength()
    {
        if (_contentHeaders.TryGetValue("Content-Length", out var values) &&
            values.Count > 0 &&
            int.TryParse(values[0], out var length))
        {
            return length;
        }
        return -1;
    }

    /// <summary>
    /// Disposes resources used by the builder.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_reader != null)
        {
            await _reader.CompleteAsync();
            _reader = null;
        }

        if (_response != null)
        {
            _response.Dispose();
            _response = null;
        }

        _contentHeaders?.Clear();
    }
}
