using RuriLib.Http.Helpers;
using RuriLib.Http.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipelines;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace RuriLib.Http;

/// <summary>
/// High-performance HTTP response builder with optimized memory management and reduced allocations.
/// </summary>
internal class HttpResponseBuilder
{
    private PipeReader reader;
    private static readonly byte[] CRLFCRLF_Bytes = [13, 10, 13, 10];
    private HttpResponse response;

    private Dictionary<string, List<string>> contentHeaders;
    private int contentLength = -1;
    private bool reusableFraming;

    /// <summary>
    /// Add ArrayPool
    /// </summary>
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    private static readonly ConcurrentQueue<StringBuilder> _stringBuilderPool = new();
    private static readonly ConcurrentQueue<Dictionary<string, string>> _headerDictionaryPool = new();

    // Pre-compiled byte sequences for performance
    private static readonly ReadOnlyMemory<byte> CrLf = "\r\n"u8.ToArray();
    private static readonly ReadOnlyMemory<byte> DoubleCrLf = "\r\n\r\n"u8.ToArray();
    private static readonly ReadOnlyMemory<byte> ChunkedEndMarker = "0\r\n\r\n"u8.ToArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StringBuilder GetPooledStringBuilder()
    {
        if (_stringBuilderPool.TryDequeue(out var sb))
        {
            sb.Clear();
            return sb;
        }
        return new StringBuilder(256);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReturnStringBuilder(StringBuilder sb)
    {
        if (sb.Capacity <= 4096) // Prevent memory bloat
        {
            _stringBuilderPool.Enqueue(sb);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Dictionary<string, string> GetPooledHeaderDictionary()
    {
        if (_headerDictionaryPool.TryDequeue(out var dict))
        {
            dict.Clear();
            return dict;
        }
        return new Dictionary<string, string>(16, StringComparer.OrdinalIgnoreCase);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReturnHeaderDictionary(Dictionary<string, string> dict)
    {
        if (dict.Count <= 32) // Prevent memory bloat
        {
            _headerDictionaryPool.Enqueue(dict);
        }
    }

    /// <summary>
    /// Nested PooledMemoryStream for ArrayPool integration
    /// </summary>
    /// <param name="buffer"></param>
    private sealed class PooledMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        private byte[] _buffer = buffer;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (_buffer != null)
            {
                _bufferPool.Return(_buffer, clearArray: false);
                _buffer = null; // Prevent double disposal
            }
        }
    }

    internal TimeSpan ReceiveTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal HttpResponseBuilder()
    {
    }

    /// <summary>
    /// Builds an HttpResponse by reading a network stream with optimized performance.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="pipeReader"></param>
    /// <param name="readResponseContent"></param>
    /// <param name="cancellationToken"></param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal async Task<HttpResponse> GetResponseAsync(HttpRequest request, PipeReader pipeReader,
        bool readResponseContent = true, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CreateReceiveTimeoutTokenSource();
        using var combinedCts = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var token = combinedCts.Token;
        var readerOwnedByResponseContent = false;

        reader = pipeReader;

        response = new HttpResponse
        {
            Request = request
        };

        contentHeaders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await ReceiveFirstLineAsync(token).ConfigureAwait(false);
            await ReceiveHeadersAsync(token).ConfigureAwait(false);

            if (ResponseMustNotHaveBody(request))
            {
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                AddContentHeadersToResponseContent();
            }
            else
            {
                readerOwnedByResponseContent = await ReceiveContentAsync(readResponseContent, token).ConfigureAwait(false);
            }

            response.CanReuseConnection = readResponseContent && HasReusableFraming(request);
        }
        catch
        {
            response.Dispose();
            throw;
        }
        finally
        {
            if (!readerOwnedByResponseContent)
            {
                await reader.CompleteAsync();
            }
        }

        return response;
    }

    private CancellationTokenSource CreateReceiveTimeoutTokenSource()
    {
        if (ReceiveTimeout == TimeSpan.Zero || ReceiveTimeout == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        if (ReceiveTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReceiveTimeout),
                ReceiveTimeout,
                "ReceiveTimeout must be zero or greater, or Timeout.InfiniteTimeSpan to disable the timeout.");
        }

        return new CancellationTokenSource(ReceiveTimeout);
    }

    /// <summary>
    /// Parses the first line, for example
    /// HTTP/1.1 200 OK
    /// </summary>
    /// <param name="cancellationToken"></param>
    private async Task ReceiveFirstLineAsync(CancellationToken cancellationToken = default)
    {
        var startingLine = string.Empty;

        // Read the first line from the Network Stream
        while (true)
        {
            var res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            var buff = res.Buffer;
            if (TryReadLine(buff, out startingLine, out var bytesConsumed))
            {
                try
                {
                    var fields = startingLine.Split(' ');
                    response.Version = Version.Parse(fields[0].Trim()[5..]);
                    response.StatusCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), fields[1]);
                    reader.AdvanceTo(buff.GetPosition(bytesConsumed));
                    break;
                }
                catch
                {
                    throw new FormatException($"Invalid first line of the HTTP response: {startingLine}");
                }
            }
            else
            {
                // the response is incomplete ex. (HTTP/1.1 200 O)
                reader.AdvanceTo(buff.Start, buff.End); // nothing consumed but all the buffer examined loop and read more.
            }
            if (res.IsCanceled || res.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                break;
            }
        }
    }

    /// <summary>
    /// Parses the headers with optimized performance using pooled dictionaries
    /// </summary>
    /// <param name="cancellationToken"></param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private async Task ReceiveHeadersAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            var buff = res.Buffer;
            if (buff.IsSingleSegment)
            {
                if (ReadHeadersFastPath(ref buff))
                {
                    reader.AdvanceTo(buff.Start);
                    break;
                }
            }
            else if (ReadHeadersSlowerPath(ref buff))
            {
                reader.AdvanceTo(buff.Start);
                break;
            }
            reader.AdvanceTo(buff.Start, buff.End); // not adding this line might result in infinite loop.
            if (res.IsCanceled || res.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                break;
            }
        }
    }

    /// <summary>
    /// Reads all Header Lines using <see cref="Span{T}"/> For High Performance Parsing.
    /// </summary>
    /// <param name="buff">Buffered Data From Pipe</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool ReadHeadersFastPath(ref ReadOnlySequence<byte> buff)
    {
        int endofheadersindex;

        if ((endofheadersindex = buff.FirstSpan.IndexOf(CRLFCRLF_Bytes)) > -1)
        {
            var spanLines = buff.FirstSpan[..(endofheadersindex + 4)];
            // we use spanHelper class here to make a for each loop.

            foreach (var Line in spanLines.SplitLines())
            {
                ProcessHeaderLine(Line);
            }

            buff = buff.Slice(endofheadersindex + 4); // add 4 bytes for \r\n\r\n and to advance the pipe back in the calling method
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads all Header Lines using SequenceReader with optimized performance.
    /// </summary>
    /// <param name="buff">Buffered Data From Pipe</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool ReadHeadersSlowerPath(ref ReadOnlySequence<byte> buff)
    {
        var sequenceReader = new SequenceReader<byte>(buff);
        var sb = GetPooledStringBuilder();

        try
        {
            while (sequenceReader.TryReadTo(out ReadOnlySpan<byte> Line, CrLf.Span, true))
            {
                if (Line.Length == 0)// reached last crlf (empty line)
                {
                    buff = buff.Slice(sequenceReader.Position);
                    return true;// all headers received
                }
                ProcessHeaderLine(Line);
            }
        }
        finally
        {
            ReturnStringBuilder(sb);
        }

        buff = buff.Slice(sequenceReader.Position);
        return false;// empty line not found need more data
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ProcessHeaderLine(ReadOnlySpan<byte> header)
    {
        if (header.Length == 0)
        {
            return;
        }

        var separatorPos = header.IndexOf((byte)':');

        if (separatorPos == -1)
        {
            return;
        }

        // Use pooled StringBuilder for efficient string building
        var sb = GetPooledStringBuilder();

        try
        {
            // Parse header name with manual trimming to avoid allocations
            var headerNameSpan = header[..separatorPos];
            var nameStart = 0;
            var nameEnd = headerNameSpan.Length - 1;

            while (nameStart <= nameEnd && headerNameSpan[nameStart] == ' ') nameStart++;
            while (nameEnd >= nameStart && headerNameSpan[nameEnd] == ' ') nameEnd--;

            if (nameStart > nameEnd) return;

            sb.Clear();
            for (int i = nameStart; i <= nameEnd; i++)
            {
                sb.Append((char)headerNameSpan[i]);
            }
            var headerName = sb.ToString();

            // Parse header value with manual trimming
            var headerValueSpan = header[(separatorPos + 1)..];
            var valueStart = 0;
            var valueEnd = headerValueSpan.Length - 1;

            while (valueStart <= valueEnd && headerValueSpan[valueStart] == ' ') valueStart++;
            while (valueEnd >= valueStart && headerValueSpan[valueEnd] == ' ') valueEnd--;

            sb.Clear();
            if (valueStart <= valueEnd)
            {
                for (int i = valueStart; i <= valueEnd; i++)
                {
                    sb.Append((char)headerValueSpan[i]);
                }
            }
            var headerValue = sb.ToString();

            // If the header is Set-Cookie, add the cookie
            if (headerName.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
                headerName.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase))
            {
                SetCookie(response, headerValue);
                AddGeneralHeader(headerName, headerValue);
            }
            // If it's a content header
            else if (IsContentHeader(headerName))
            {
                TrackReusableFraming(headerName, headerValue);
                AddContentHeader(headerName, headerValue);
            }
            else
            {
                TrackReusableFraming(headerName, headerValue);
                AddGeneralHeader(headerName, headerValue);
            }
        }
        finally
        {
            ReturnStringBuilder(sb);
        }
    }

    private static bool IsContentHeader(string headerName) =>
        headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
        headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
        headerName.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase) ||
        headerName.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase) ||
        headerName.Equals("Content-Location", StringComparison.OrdinalIgnoreCase) ||
        headerName.Equals("Content-Range", StringComparison.OrdinalIgnoreCase) ||
        headerName.Equals("Last-Modified", StringComparison.OrdinalIgnoreCase) ||
        headerName.Equals("Expires", StringComparison.OrdinalIgnoreCase);

    private void TrackReusableFraming(string headerName, string headerValue)
    {
        if (headerName.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
            headerValue.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            reusableFraming = true;
        }
    }

    private void AddContentHeader(string headerName, string headerValue)
    {
        if (contentHeaders.ContainsKey(headerName))
        {
            contentHeaders[headerName].Add(headerValue);
        }
        else
        {
            contentHeaders.Add(headerName, [headerValue]);
        }
    }

    private void AddGeneralHeader(string headerName, string headerValue)
    {
        if (!response.Headers.TryAdd(headerName, headerValue))
        {
            response.Headers[headerName] += ", " + headerValue;
        }
    }

    /// <summary>
    /// Sets the value of a cookie
    /// </summary>
    /// <param name="response"></param>
    /// <param name="value"></param>
    private static void SetCookie(HttpResponse response, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var cookies = ParseCookies(value);
        foreach (var cookie in cookies)
        {
            response.Request.Cookies[cookie.Key] = cookie.Value;
        }
    }

    private static Dictionary<string, string> ParseCookies(string cookieHeader)
    {
        var cookies = new Dictionary<string, string>();
        var start = 0;
        var inQuotes = false;

        for (var i = 0; i <= cookieHeader.Length; i++)
        {
            var atEnd = i == cookieHeader.Length;
            var c = atEnd ? ',' : cookieHeader[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }

            if ((c == ',' && !inQuotes) || atEnd)
            {
                var length = i - start;
                if (length > 0)
                {
                    var segment = cookieHeader.Substring(start, length).Trim();
                    var cookie = ParseSingleCookie(segment);
                    if (cookie.HasValue)
                    {
                        cookies[cookie.Value.Key] = cookie.Value.Value;
                    }
                }
                start = i + 1;
            }
        }

        return cookies;
    }

    private static KeyValuePair<string, string>? ParseSingleCookie(string segment)
    {
        var eqPos = segment.IndexOf('=');
        if (eqPos <= 0)
        {
            return null;
        }

        var name = segment[..eqPos].Trim();
        var semiPos = segment.IndexOf(';', eqPos + 1);
        var val = semiPos == -1
            ? segment[(eqPos + 1)..].Trim()
            : segment.Substring(eqPos + 1, semiPos - eqPos - 1).Trim();

        // Remove quotes around value
        if (val.Length >= 2 && val[0] == '"' && val[^1] == '"')
        {
            val = val[1..^1];
        }

        return new KeyValuePair<string, string>(name, val);
    }

    private async Task<bool> ReceiveContentAsync(bool readResponseContent = true, CancellationToken cancellationToken = default)
    {
        var readerOwnedByResponseContent = false;

        if (readResponseContent)
        {
            // Existing logic to read content into a stream
            var sourceStream = await GetMessageBodySource(cancellationToken).ConfigureAwait(false);
            response.Content = new StreamContent(sourceStream);
        }
        else
        {
            // Create a PipeReaderStream for on-demand reading
            response.Content = new StreamContent(new PipeReaderStream(reader, leaveOpen: false));
            readerOwnedByResponseContent = true;
        }

        AddContentHeadersToResponseContent();

        return readerOwnedByResponseContent;
    }

    private void AddContentHeadersToResponseContent()
    {
        foreach (var header in contentHeaders)
        {
            if (!response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                response.Headers.Add(header.Key, string.Join(", ", header.Value));
            }
        }
    }

    private Task<Stream> GetMessageBodySource(CancellationToken cancellationToken) =>
        response.Headers.TryGetValue("Transfer-Encoding", out var value) &&
        value.Contains("chunked", StringComparison.OrdinalIgnoreCase)
            ? GetChunkedDecompressedStream(cancellationToken)
            : GetContentLength() != -1
            ? GetContentLengthDecompressedStream(cancellationToken)
            : GetResponcestreamUntilCloseDecompressed(cancellationToken);

    private bool HasReusableFraming(HttpRequest request)
    {
        if (ResponseMustNotHaveBody(request))
        {
            return true;
        }

        return reusableFraming || GetContentLength() >= 0;
    }

    private bool ResponseMustNotHaveBody(HttpRequest request)
    {
        var statusCode = (int)response.StatusCode;
        return request.Method == HttpMethod.Head ||
               statusCode is >= 100 and < 200 or 204 or 304;
    }

    private async Task<Stream> GetResponcestreamUntilClose(CancellationToken cancellationToken)
    {
        var ms = new MemoryStream();

        try
        {
            while (true)
            {
                var res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buff = res.Buffer;

                if (buff.IsEmpty && res.IsCompleted)
                {
                    break;
                }

                foreach (var segment in buff)
                {
                    await ms.WriteAsync(segment.Span.ToArray(), cancellationToken);
                }

                reader.AdvanceTo(buff.End);

                if (res.IsCompleted)
                {
                    break;
                }
            }
        }
        catch
        {
            await ms.DisposeAsync();
            throw;
        }

        ms.Position = 0;
        return ms;
    }

    private async Task<Stream> GetContentLengthDecompressedStream(CancellationToken cancellationToken) =>
        GetZipStream(await ReciveContentLength(cancellationToken).ConfigureAwait(false));

    private async Task<Stream> GetChunkedDecompressedStream(CancellationToken cancellationToken) =>
        GetZipStream(await ReceiveMessageBodyChunked(cancellationToken).ConfigureAwait(false));

    private async Task<Stream> GetResponcestreamUntilCloseDecompressed(CancellationToken cancellationToken) =>
        GetZipStream(await GetResponcestreamUntilClose(cancellationToken).ConfigureAwait(false));

    private async Task<Stream> ReciveContentLength(CancellationToken cancellationToken)
    {
        var length = GetContentLength();

        if (length < 0)
        {
            throw new InvalidOperationException("Cannot read content by length when length is negative");
        }

        var ms = new MemoryStream(length);

        long bytesRead = 0;

        try
        {
            while (bytesRead < length)
            {
                var res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buff = res.Buffer;
                var bytesToCopy = Math.Min(buff.Length, length - bytesRead);

                await CopyToStreamAsync(ms, buff, bytesToCopy, cancellationToken).ConfigureAwait(false);
                reader.AdvanceTo(buff.GetPosition(bytesToCopy));
                bytesRead += bytesToCopy;

                if (res.IsCompleted && bytesRead < length)
                {
                    throw new EndOfStreamException("Reached end of stream before expected content length");
                }
            }
        }
        catch
        {
            await ms.DisposeAsync();
            throw;
        }

        ms.Position = 0;
        return ms;
    }

    private int GetContentLength()
    {
        if (contentLength != -1 || !contentHeaders.TryGetValue("Content-Length", out var values))
        {
            return contentLength;
        }

        int? parsedLength = null;
        foreach (var headerValue in values)
        {
            foreach (var part in headerValue.Split(','))
            {
                if (!int.TryParse(part.Trim(), out var candidate) || candidate < 0)
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

        contentLength = parsedLength ?? -1;
        return contentLength;
    }

    private string GetContentEncoding() =>
        contentHeaders.TryGetValue("Content-Encoding", out var value) ? value[0] : string.Empty;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private async Task<Stream> ReceiveMessageBodyChunked(CancellationToken cancellationToken)
    {
        var ms = new MemoryStream();

        try
        {
            while (true)
            {
                var chunkSize = await ReadChunkSizeAsync(cancellationToken);
                if (chunkSize == 0)
                {
                    await SkipTrailingHeadersAsync(cancellationToken);
                    break;
                }

                await ReadChunkDataAsync(ms, chunkSize, cancellationToken);
                await SkipChunkCrlfAsync(cancellationToken);
            }
        }
        catch
        {
            await ms.DisposeAsync();
            throw;
        }

        ms.Position = 0;
        return ms;
    }

    private async Task<int> ReadChunkSizeAsync(CancellationToken cancellationToken)
    {
        var chunkSizeLine = await ReadLineAsync(cancellationToken);
        return int.Parse(chunkSizeLine.Split(';')[0].Trim(), System.Globalization.NumberStyles.HexNumber);
    }

    private async Task ReadChunkDataAsync(MemoryStream destination, int chunkSize, CancellationToken cancellationToken)
    {
        long bytesRead = 0;
        while (bytesRead < chunkSize)
        {
            var res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buff = res.Buffer;

            var bytesToCopy = Math.Min(buff.Length, chunkSize - bytesRead);
            await CopyToStreamAsync(destination, buff, bytesToCopy, cancellationToken).ConfigureAwait(false);
            reader.AdvanceTo(buff.GetPosition(bytesToCopy));
            bytesRead += bytesToCopy;

            if (res.IsCompleted && bytesRead < chunkSize)
            {
                throw new EndOfStreamException("Reached end of stream before expected chunk size");
            }
        }
    }

    private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buff = res.Buffer;
            if (TryReadLine(buff, out var line, out var bytesConsumed))
            {
                reader.AdvanceTo(buff.GetPosition(bytesConsumed));
                return line;
            }

            reader.AdvanceTo(buff.Start, buff.End);

            if (res.IsCanceled || res.IsCompleted)
            {
                throw new EndOfStreamException("Reached end of stream before line");
            }
        }
    }

    private async Task SkipTrailingHeadersAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line.Length == 0)
            {
                return;
            }
        }
    }

    private async Task SkipChunkCrlfAsync(CancellationToken cancellationToken)
    {
        var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line.Length != 0)
        {
            throw new FormatException("Invalid chunk terminator");
        }
    }

    private static async Task CopyToStreamAsync(
        Stream destination,
        ReadOnlySequence<byte> buffer,
        long bytesToCopy,
        CancellationToken cancellationToken)
    {
        var remaining = bytesToCopy;

        foreach (var segment in buffer)
        {
            if (remaining <= 0)
            {
                break;
            }

            var segmentLength = (int)Math.Min(segment.Length, remaining);
            await destination.WriteAsync(segment[..segmentLength], cancellationToken).ConfigureAwait(false);
            remaining -= segmentLength;
        }
    }

    private bool TryReadLine(ReadOnlySequence<byte> buffer, out string line, out long bytesConsumed)
    {
        var reader = new SequenceReader<byte>(buffer);
        if (reader.TryReadTo(out ReadOnlySequence<byte> lineSequence, CrLf.Span))
        {
            line = lineSequence.IsSingleSegment
                ? Encoding.UTF8.GetString(lineSequence.FirstSpan)
                : Encoding.UTF8.GetString(lineSequence.ToArray());
            bytesConsumed = reader.Consumed;
            return true;
        }

        line = string.Empty;
        bytesConsumed = 0;
        return false;
    }

    private Stream GetZipStream(Stream stream)
    {
        var encoding = GetContentEncoding();
        if (encoding.Contains("br"))
        {
            return new BrotliStream(stream, CompressionMode.Decompress);
        }
        else if (encoding.Contains("gzip"))
        {
            return new GZipStream(stream, CompressionMode.Decompress);
        }
        else if (encoding.Contains("deflate"))
        {
            return new DeflateStream(stream, CompressionMode.Decompress);
        }
        return stream;
    }

}
