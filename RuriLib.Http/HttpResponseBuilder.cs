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
internal class HttpResponseBuilder : IDisposable
{
    private PipeReader reader;
    private const string newLine = "\r\n";
    private readonly byte[] CRLF = Encoding.UTF8.GetBytes(newLine);
    private static readonly byte[] CRLFCRLF_Bytes = [13, 10, 13, 10];
    private HttpResponse response;

    private Dictionary<string, List<string>> contentHeaders;
    private int contentLength = -1;

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
        using var timeoutCts = new CancellationTokenSource(ReceiveTimeout);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var token = combinedCts.Token;

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

            if (request.Method != HttpMethod.Head)
            {
                await ReceiveContentAsync(readResponseContent, token).ConfigureAwait(false);
            }
        }
        catch
        {
            response.Dispose();
            throw;
        }
        finally
        {
            if (readResponseContent)
            {
                // Only complete the reader if the content was fully read and buffered
                await reader.CompleteAsync();
            }
            // If readResponseContent is false, PipeReaderStream will complete the reader upon its disposal.
        }

        return response;
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
            var crlfIndex = buff.FirstSpan.IndexOf(CRLF);
            if (crlfIndex > -1)
            {
                try
                {
                    startingLine = Encoding.UTF8.GetString(res.Buffer.FirstSpan[..crlfIndex]);
                    var fields = startingLine.Split(' ');
                    response.Version = Version.Parse(fields[0].Trim()[5..]);
                    response.StatusCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), fields[1]);
                    buff = buff.Slice(0, crlfIndex + 2); // add 2 bytes for the CRLF
                    reader.AdvanceTo(buff.End); // advance to the consumed position
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
            while (sequenceReader.TryReadTo(out ReadOnlySpan<byte> Line, CRLF, true))
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
            }
            // If it's a content header
            else if (IsContentHeader(headerName))
            {
                AddContentHeader(headerName, headerValue);
            }
            else
            {
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

    private async Task ReceiveContentAsync(bool readResponseContent = true, CancellationToken cancellationToken = default)
    {
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
        }

        // Set content headers from the collected dictionary
        foreach (var header in contentHeaders)
        {
            if (!response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                // If it's a content header that was added to contentHeaders, it should be a List<string>
                // Join the list into a single string before adding to response.Headers
                response.Headers.Add(header.Key, string.Join(", ", header.Value));
            }
        }
    }

    private Task<Stream> GetMessageBodySource(CancellationToken cancellationToken) =>
        response.Headers.TryGetValue("Transfer-Encoding", out var value) &&
        value.Equals("chunked", StringComparison.OrdinalIgnoreCase)
            ? GetChunkedDecompressedStream(cancellationToken)
            : GetContentLength() != -1
            ? GetContentLengthDecompressedStream(cancellationToken)
            : GetResponcestreamUntilCloseDecompressed(cancellationToken);

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

        // If the content is small, use a regular MemoryStream, otherwise use a pooled one.
        // The threshold 4096 is arbitrary and can be tuned.
        var ms = length > 4096
            ? new PooledMemoryStream(_bufferPool.Rent(length))
            : new MemoryStream();

        long bytesRead = 0;

        try
        {
            while (bytesRead < length)
            {
                var res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buff = res.Buffer;

                var bytesToCopy = Math.Min(buff.Length, length - bytesRead);

                foreach (var segment in buff)
                {
                    await ms.WriteAsync(segment.Span.ToArray(), cancellationToken);
                    bytesRead += segment.Length;
                }

                reader.AdvanceTo(buff.End);

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
        if (contentLength == -1 && contentHeaders.ContainsKey("Content-Length"))
        {
            var value = contentHeaders["Content-Length"][0];
            if (int.TryParse(value, out var parsedLength))
            {
                contentLength = parsedLength;
            }
        }
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
                    await SkipTrailingCrlfAsync(cancellationToken);
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
        return int.Parse(chunkSizeLine.Split(';')[0], System.Globalization.NumberStyles.HexNumber);
    }

    private async Task ReadChunkDataAsync(MemoryStream destination, int chunkSize, CancellationToken cancellationToken)
    {
        long bytesRead = 0;
        while (bytesRead < chunkSize)
        {
            var res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buff = res.Buffer;

            var bytesToCopy = Math.Min(buff.Length, chunkSize - bytesRead);

            foreach (var segment in buff)
            {
                await destination.WriteAsync(segment.Span.ToArray(), cancellationToken);
                bytesRead += segment.Length;
            }
            reader.AdvanceTo(buff.GetPosition(bytesRead));

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
            var crlfIndex = buff.FirstSpan.IndexOf(CRLF);

            if (crlfIndex > -1)
            {
                var line = Encoding.UTF8.GetString(buff.FirstSpan[..crlfIndex]);
                reader.AdvanceTo(buff.GetPosition(crlfIndex + 2));
                return line;
            }

            reader.AdvanceTo(buff.Start, buff.End);

            if (res.IsCanceled || res.IsCompleted)
            {
                throw new EndOfStreamException("Reached end of stream before line");
            }
        }
    }

    private async Task SkipTrailingCrlfAsync(CancellationToken cancellationToken)
    {
        await ReadLineAsync(cancellationToken);
    }

    private async Task SkipChunkCrlfAsync(CancellationToken cancellationToken)
    {
        await ReadLineAsync(cancellationToken);
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

    public void Dispose() => throw new NotImplementedException();
}
