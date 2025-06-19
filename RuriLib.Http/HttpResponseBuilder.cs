using RuriLib.Http.Helpers;
using RuriLib.Http.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipelines;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace RuriLib.Http
{
    internal class HttpResponseBuilder
    {
        private PipeReader reader;
        private const string newLine = "\r\n";
        private readonly byte[] CRLF = Encoding.UTF8.GetBytes(newLine);
        private static byte[] CRLFCRLF_Bytes = { 13, 10, 13, 10 };
        private HttpResponse response;
    
        private Dictionary<string, List<string>> contentHeaders;
        private int contentLength = -1;

        // Add ArrayPool
        private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

        // Nested PooledMemoryStream for ArrayPool integration
        private class PooledMemoryStream : MemoryStream
        {
            private byte[] _buffer;

            public PooledMemoryStream(byte[] buffer) : base(buffer)
            {
                _buffer = buffer;
            }

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
            //  pipe = new Pipe();
        }

        /// <summary>
        /// Builds an HttpResponse by reading a network stream.
        /// </summary>
        [MethodImpl(methodImplOptions: MethodImplOptions.AggressiveOptimization)]
        async internal Task<HttpResponse> GetResponseAsync(HttpRequest request, PipeReader pipeReader,
            bool readResponseContent = true, CancellationToken cancellationToken = default)
        {
            reader = pipeReader;

            response = new HttpResponse
            {
                Request = request
            };

            contentHeaders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                await ReceiveFirstLineAsync(cancellationToken).ConfigureAwait(false);
                await ReceiveHeadersAsync(cancellationToken).ConfigureAwait(false);

                if (request.Method != HttpMethod.Head)
                {
                    await ReceiveContentAsync(readResponseContent, cancellationToken).ConfigureAwait(false);
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
                    reader.Complete();
                }
                // If readResponseContent is false, PipeReaderStream will complete the reader upon its disposal.
            }

            return response;
        }

        // Parses the first line, for example
        // HTTP/1.1 200 OK
        private async Task ReceiveFirstLineAsync(CancellationToken cancellationToken = default)
        {
            var startingLine = string.Empty;

            // Read the first line from the Network Stream
            while (true)
            {
                var res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                var buff = res.Buffer;
                int crlfIndex = buff.FirstSpan.IndexOf(CRLF);
                if (crlfIndex > -1)
                {
                    try
                    {
                        startingLine = Encoding.UTF8.GetString(res.Buffer.FirstSpan.Slice(0, crlfIndex));
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
                    // the responce is incomplete ex. (HTTP/1.1 200 O)
                    reader.AdvanceTo(buff.Start, buff.End); // nothing consumed but all the buffer examined loop and read more.
                }
                if (res.IsCanceled || res.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    break;
                }
            }
        }

        // Parses the headers
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
                else
                {
                    if (ReadHeadersSlowerPath(ref buff))
                    {
                        reader.AdvanceTo(buff.Start);
                        break;
                    }
                }
                reader.AdvanceTo(buff.Start, buff.End); // not adding this line might result in infinit loop.
                if (res.IsCanceled || res.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    break;
                }
            }
        }


        /// <summary>
        /// Reads all Header Lines using <see cref="Span{T}"/> For High Perfromace Parsing.
        /// </summary>
        /// <param name="buff">Buffered Data From Pipe</param>
        private bool ReadHeadersFastPath(ref ReadOnlySequence<byte> buff)
        {
            int endofheadersindex;

            if ((endofheadersindex = buff.FirstSpan.IndexOf(CRLFCRLF_Bytes)) > -1)
            {
                var spanLines = buff.FirstSpan.Slice(0, endofheadersindex + 4);
                var Lines = spanLines.SplitLines();// we use spanHelper class here to make a for each loop.

                foreach (var Line in Lines)
                {
                    ProcessHeaderLine(Line);
                }

                buff = buff.Slice(endofheadersindex + 4); // add 4 bytes for \r\n\r\n and to advance the pipe back in the calling method
                return true;
            }

            return false;
        }
        /// <summary>
        /// Reads all Header Lines using SequenceReader.
        /// </summary>
        /// <param name="buff">Buffered Data From Pipe</param>
        private bool ReadHeadersSlowerPath(ref ReadOnlySequence<byte> buff)
        {
            var reader = new SequenceReader<byte>(buff);

            while (reader.TryReadTo(out ReadOnlySpan<byte> Line, CRLF, true))
            {
                if (Line.Length == 0)// reached last crlf (empty line)
                {
                    buff = buff.Slice(reader.Position);
                    return true;// all headers received
                }
                ProcessHeaderLine(Line);
            }

            buff = buff.Slice(reader.Position);
            return false;// empty line not found need more data
        }

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

            // Slice and trim the header name span
            var headerNameSpan = header.Slice(0, separatorPos).Trim((byte)' '); // Trim any leading/trailing spaces
            var headerValueSpan = header.Slice(separatorPos + 1).Trim((byte)' '); // Skip ':' and trim leading/trailing spaces

            var headerName = Encoding.UTF8.GetString(headerNameSpan);
            var headerValue = Encoding.UTF8.GetString(headerValueSpan);

            // If the header is Set-Cookie, add the cookie
            if (headerName.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
                headerName.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase))
            {
                SetCookie(response, headerValue);
            }
            // If it's a content header
            else if (headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                     headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                     headerName.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase) ||
                     headerName.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase) ||
                     headerName.Equals("Content-Location", StringComparison.OrdinalIgnoreCase) ||
                     headerName.Equals("Content-Range", StringComparison.OrdinalIgnoreCase) ||
                     headerName.Equals("Last-Modified", StringComparison.OrdinalIgnoreCase) ||
                     headerName.Equals("Expires", StringComparison.OrdinalIgnoreCase))
            {
                // These content headers are specifically handled in HttpResponseBuilder (e.g., Content-Length sets contentLength field)
                // Add to contentHeaders for potential further processing
                if (contentHeaders.ContainsKey(headerName))
                {
                    contentHeaders[headerName].Add(headerValue);
                }
                else
                {
                    contentHeaders.Add(headerName, new List<string> { headerValue });
                }
            }
            else
            {
                // Add to general response headers
                if (response.Headers.ContainsKey(headerName))
                {
                    // If header already exists, append new value (for multi-value headers)
                    response.Headers[headerName] += ", " + headerValue;
                }
                else
                {
                    response.Headers.Add(headerName, headerValue);
                }
            }
        }

        // Sets the value of a cookie
        private static void SetCookie(HttpResponse response, string value)
        {
            if (value.Length == 0)
            {
                return;
            }

            var endCookiePos = value.IndexOf(';');
            var separatorPos = value.IndexOf('=');

            if (separatorPos == -1)
            {
                // Invalid cookie, simply don't add it
                return;
            }

            string cookieValue;
            var cookieName = value.Substring(0, separatorPos);

            if (endCookiePos == -1)
            {
                cookieValue = value[(separatorPos + 1)..];
            }
            else
            {
                cookieValue = value.Substring(separatorPos + 1, (endCookiePos - separatorPos) - 1);
            }

            response.Request.Cookies[cookieName] = cookieValue;
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

        private Task<Stream> GetMessageBodySource(CancellationToken cancellationToken)
        {
            // Chunked
            if (response.Headers.ContainsKey("Transfer-Encoding") &&
                response.Headers["Transfer-Encoding"].Equals("chunked", StringComparison.OrdinalIgnoreCase))
            {
                return GetChunkedDecompressedStream(cancellationToken);
            }
            // Content-Length given
            else if (GetContentLength() != -1)
            {
                return GetContentLengthDecompressedStream(cancellationToken);
            }
            // Neither content-length nor transfer-encoding is given, read until the connection is closed
            else
            {
                return GetResponcestreamUntilCloseDecompressed(cancellationToken);
            }
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
                        ms.Write(segment.Span);
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
                ms.Dispose();
                throw;
            }

            ms.Position = 0;
            return ms;
        }

        private async Task<Stream> GetContentLengthDecompressedStream(CancellationToken cancellationToken)
        {
            return GetZipStream(await ReciveContentLength(cancellationToken).ConfigureAwait(false));
        }

        private async Task<Stream> GetChunkedDecompressedStream(CancellationToken cancellationToken)
        {
            return GetZipStream(await ReceiveMessageBodyChunked(cancellationToken).ConfigureAwait(false));
        }

        private async Task<Stream> GetResponcestreamUntilCloseDecompressed(CancellationToken cancellationToken)
        {
            return GetZipStream(await GetResponcestreamUntilClose(cancellationToken).ConfigureAwait(false));
        }

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
                    ReadResult res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    ReadOnlySequence<byte> buff = res.Buffer;

                    long bytesToCopy = Math.Min(buff.Length, length - bytesRead);

                    foreach (var segment in buff)
                    {
                        var spanToCopy = segment.Span;
                        if (spanToCopy.Length > bytesToCopy)
                        {
                            spanToCopy = spanToCopy.Slice(0, (int)bytesToCopy);
                        }
                        ms.Write(spanToCopy);
                        bytesRead += spanToCopy.Length;
                        bytesToCopy -= spanToCopy.Length;
                        if (bytesToCopy == 0 && bytesRead == length) break; // copied all the requested amount
                    }
                    
                    reader.AdvanceTo(buff.GetPosition(bytesRead)); // advance the pipe for the read bytes

                    if (res.IsCompleted && bytesRead < length)
                    {
                        throw new EndOfStreamException("Reached end of stream before expected content length");
                    }
                }
            }
            catch
            {
                ms.Dispose();
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
                if (int.TryParse(value, out int parsedLength))
                {
                    contentLength = parsedLength;
                }
            }
            return contentLength;
        }

        private string GetContentEncoding()
        {
            if (contentHeaders.ContainsKey("Content-Encoding"))
            {
                return contentHeaders["Content-Encoding"][0];
            }
            return string.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private async Task<Stream> ReceiveMessageBodyChunked(CancellationToken cancellationToken)
        {
            var ms = new MemoryStream();
            
            try
            {
                while (true)
                {
                    // Read chunk size line
                    var chunkSizeLine = string.Empty;
                    while (true)
                    {
                        ReadResult res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                        ReadOnlySequence<byte> buff = res.Buffer;
                        int crlfIndex = buff.FirstSpan.IndexOf(CRLF);

                        if (crlfIndex > -1)
                        {
                            chunkSizeLine = Encoding.UTF8.GetString(buff.FirstSpan.Slice(0, crlfIndex));
                            reader.AdvanceTo(buff.GetPosition(crlfIndex + 2));
                            break;
                        }
                        else
                        {
                            reader.AdvanceTo(buff.Start, buff.End);
                        }

                        if (res.IsCanceled || res.IsCompleted)
                        {
                            throw new EndOfStreamException("Reached end of stream before chunk size line");
                        }
                    }

                    var chunkSize = int.Parse(chunkSizeLine.Split(';')[0], System.Globalization.NumberStyles.HexNumber);
                    
                    // If chunk size is 0, we're done
                    if (chunkSize == 0)
                    {
                        // Read the trailing CRLF after the last chunk
                        var finalCrlfLine = string.Empty;
                        while (true)
                        {
                            ReadResult res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                            ReadOnlySequence<byte> buff = res.Buffer;
                            int crlfIndex = buff.FirstSpan.IndexOf(CRLF);

                            if (crlfIndex > -1)
                            {
                                finalCrlfLine = Encoding.UTF8.GetString(buff.FirstSpan.Slice(0, crlfIndex));
                                reader.AdvanceTo(buff.GetPosition(crlfIndex + 2));
                                break;
                            }
                            else
                            {
                                reader.AdvanceTo(buff.Start, buff.End);
                            }

                            if (res.IsCanceled || res.IsCompleted)
                            {
                                throw new EndOfStreamException("Reached end of stream before final CRLF");
                            }
                        }
                        break;
                    }

                    // Read chunk data
                    long bytesRead = 0;
                    while (bytesRead < chunkSize)
                    {
                        ReadResult res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                        ReadOnlySequence<byte> buff = res.Buffer;

                        long bytesToCopy = Math.Min(buff.Length, chunkSize - bytesRead);

                        foreach (var segment in buff)
                        {
                            var spanToCopy = segment.Span;
                            if (spanToCopy.Length > bytesToCopy)
                            {
                                spanToCopy = spanToCopy.Slice(0, (int)bytesToCopy);
                            }
                            ms.Write(spanToCopy);
                            bytesRead += spanToCopy.Length;
                            bytesToCopy -= spanToCopy.Length;
                            if (bytesToCopy == 0 && bytesRead == chunkSize) break; // copied all the requested amount
                        }
                        reader.AdvanceTo(buff.GetPosition(bytesRead));

                        if (res.IsCompleted && bytesRead < chunkSize)
                        {
                            throw new EndOfStreamException("Reached end of stream before expected chunk size");
                        }
                    }

                    // Read the trailing CRLF after the chunk
                    var chunkCrlfLine = string.Empty;
                    while (true)
                    {
                        ReadResult res = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                        ReadOnlySequence<byte> buff = res.Buffer;
                        int crlfIndex = buff.FirstSpan.IndexOf(CRLF);

                        if (crlfIndex > -1)
                        {
                            chunkCrlfLine = Encoding.UTF8.GetString(buff.FirstSpan.Slice(0, crlfIndex));
                            reader.AdvanceTo(buff.GetPosition(crlfIndex + 2));
                            break;
                        }
                        else
                        {
                            reader.AdvanceTo(buff.Start, buff.End);
                        }

                        if (res.IsCanceled || res.IsCompleted)
                        {
                            throw new EndOfStreamException("Reached end of stream before chunk CRLF");
                        }
                    }
                }
            }
            catch
            {
                ms.Dispose();
                throw;
            }

            ms.Position = 0;
            return ms;
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
}
