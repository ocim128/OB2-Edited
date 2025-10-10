using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RuriLib.Http.Models;

namespace RuriLib.Http;

/// <summary>
/// High-performance HTTP optimization utilities for reducing allocations and improving throughput.
/// </summary>
public static class HttpPerformanceOptimizer
{
    // Pre-compiled common HTTP strings as byte arrays for performance
    private static readonly byte[] HttpVersionBytes = "HTTP/1.1"u8.ToArray();
    private static readonly byte[] GetMethodBytes = "GET"u8.ToArray();
    private static readonly byte[] PostMethodBytes = "POST"u8.ToArray();
    private static readonly byte[] PutMethodBytes = "PUT"u8.ToArray();
    private static readonly byte[] DeleteMethodBytes = "DELETE"u8.ToArray();
    private static readonly byte[] HeadMethodBytes = "HEAD"u8.ToArray();
    private static readonly byte[] OptionsMethodBytes = "OPTIONS"u8.ToArray();
    private static readonly byte[] PatchMethodBytes = "PATCH"u8.ToArray();

    private static readonly byte[] CrLfBytes = "\r\n"u8.ToArray();
    private static readonly byte[] ColonSpaceBytes = ": "u8.ToArray();
    private static readonly byte[] SpaceBytes = " "u8.ToArray();

    // Common header names as byte arrays
    private static readonly byte[] HostHeaderBytes = "Host"u8.ToArray();
    private static readonly byte[] UserAgentHeaderBytes = "User-Agent"u8.ToArray();
    private static readonly byte[] ContentTypeHeaderBytes = "Content-Type"u8.ToArray();
    private static readonly byte[] ContentLengthHeaderBytes = "Content-Length"u8.ToArray();
    private static readonly byte[] ConnectionHeaderBytes = "Connection"u8.ToArray();
    private static readonly byte[] AcceptHeaderBytes = "Accept"u8.ToArray();
    private static readonly byte[] AuthorizationHeaderBytes = "Authorization"u8.ToArray();

    // Header value cache for common values
    private static readonly Dictionary<string, byte[]> _headerValueCache = new(StringComparer.OrdinalIgnoreCase)
    {
        ["keep-alive"] = "keep-alive"u8.ToArray(),
        ["close"] = "close"u8.ToArray(),
        ["application/json"] = "application/json"u8.ToArray(),
        ["application/x-www-form-urlencoded"] = "application/x-www-form-urlencoded"u8.ToArray(),
        ["text/html"] = "text/html"u8.ToArray(),
        ["text/plain"] = "text/plain"u8.ToArray(),
        ["*/*"] = "*/*"u8.ToArray(),
        ["gzip, deflate"] = "gzip, deflate"u8.ToArray(),
        ["en-US,en;q=0.9"] = "en-US,en;q=0.9"u8.ToArray()
    };

    /// <summary>
    /// Optimized HTTP request writing with minimal allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static async ValueTask WriteOptimizedRequestAsync(HttpRequest request, IBufferWriter<byte> bufferWriter, CancellationToken cancellationToken = default)
    {
        var writer = bufferWriter;

        // Write request line with pre-compiled byte arrays
        WriteMethodBytes(writer, request.Method);
        writer.Write(SpaceBytes);
        WriteUriPath(writer, request.Uri);
        writer.Write(SpaceBytes);
        writer.Write(HttpVersionBytes);
        writer.Write(CrLfBytes);

        // Write headers with optimized approach
        await WriteOptimizedHeadersAsync(writer, request.Headers, request.Uri, cancellationToken).ConfigureAwait(false);

        // Write content if present
        if (request.Content != null)
        {
            await WriteOptimizedContentAsync(writer, request.Content, cancellationToken).ConfigureAwait(false);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteMethodBytes(IBufferWriter<byte> writer, HttpMethod method)
    {
        var methodBytes = method.Method switch
        {
            "GET" => GetMethodBytes,
            "POST" => PostMethodBytes,
            "PUT" => PutMethodBytes,
            "DELETE" => DeleteMethodBytes,
            "HEAD" => HeadMethodBytes,
            "OPTIONS" => OptionsMethodBytes,
            "PATCH" => PatchMethodBytes,
            _ => Encoding.ASCII.GetBytes(method.Method)
        };

        writer.Write(methodBytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void WriteUriPath(IBufferWriter<byte> writer, Uri uri)
    {
        var pathAndQuery = uri.PathAndQuery;
        if (string.IsNullOrEmpty(pathAndQuery) || pathAndQuery == "/")
        {
            writer.Write("/"u8);
            return;
        }

        // Use pooled byte array for URI encoding
        var buffer = MemoryPoolUtility.RentByteArray(pathAndQuery.Length * 3); // Worst case for URL encoding
        try
        {
            var bytesWritten = Encoding.UTF8.GetBytes(pathAndQuery, buffer);
            writer.Write(buffer.AsSpan(0, bytesWritten));
        }
        finally
        {
            MemoryPoolUtility.ReturnByteArray(buffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static async ValueTask WriteOptimizedHeadersAsync(IBufferWriter<byte> writer, Dictionary<string, string> headers, Uri uri, CancellationToken cancellationToken)
    {
        // Ensure Host header is present
        if (!headers.ContainsKey("Host"))
        {
            WriteHeaderLine(writer, "Host", uri.Host);
        }

        // Write all headers with optimized byte operations
        foreach (var header in headers)
        {
            WriteHeaderLine(writer, header.Key, header.Value);
        }

        // End headers section
        writer.Write(CrLfBytes);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteHeaderLine(IBufferWriter<byte> writer, string name, string value)
    {
        // Try to use cached header name bytes
        var nameBytes = GetCachedHeaderNameBytes(name);
        if (nameBytes != null)
        {
            writer.Write(nameBytes);
        }
        else
        {
            WriteStringAsBytes(writer, name);
        }

        writer.Write(ColonSpaceBytes);

        // Try to use cached header value bytes
        if (_headerValueCache.TryGetValue(value, out var valueBytes))
        {
            writer.Write(valueBytes);
        }
        else
        {
            WriteStringAsBytes(writer, value);
        }

        writer.Write(CrLfBytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] GetCachedHeaderNameBytes(string headerName)
    {
        return headerName switch
        {
            "Host" => HostHeaderBytes,
            "User-Agent" => UserAgentHeaderBytes,
            "Content-Type" => ContentTypeHeaderBytes,
            "Content-Length" => ContentLengthHeaderBytes,
            "Connection" => ConnectionHeaderBytes,
            "Accept" => AcceptHeaderBytes,
            "Authorization" => AuthorizationHeaderBytes,
            _ => null
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteStringAsBytes(IBufferWriter<byte> writer, string str)
    {
        if (string.IsNullOrEmpty(str))
            return;

        var buffer = MemoryPoolUtility.RentByteArray(Encoding.UTF8.GetMaxByteCount(str.Length));
        try
        {
            var bytesWritten = Encoding.UTF8.GetBytes(str, buffer);
            writer.Write(buffer.AsSpan(0, bytesWritten));
        }
        finally
        {
            MemoryPoolUtility.ReturnByteArray(buffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static async ValueTask WriteOptimizedContentAsync(IBufferWriter<byte> writer, HttpContent content, CancellationToken cancellationToken)
    {
        if (content == null)
            return;

        // Handle different content types efficiently (most specific first)
        switch (content)
        {
            case StringContent stringContent:
                await WriteStringContentAsync(writer, stringContent, cancellationToken).ConfigureAwait(false);
                break;

            case StreamContent streamContent:
                await WriteStreamContentAsync(writer, streamContent, cancellationToken).ConfigureAwait(false);
                break;

            case ByteArrayContent byteContent:
                await WriteByteArrayContentAsync(writer, byteContent, cancellationToken).ConfigureAwait(false);
                break;

            default:
                // Fallback to standard approach
                var contentBytes = await content.ReadAsByteArrayAsync().ConfigureAwait(false);
                writer.Write(contentBytes);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static async ValueTask WriteByteArrayContentAsync(IBufferWriter<byte> writer, ByteArrayContent content, CancellationToken cancellationToken)
    {
        var bytes = await content.ReadAsByteArrayAsync().ConfigureAwait(false);
        writer.Write(bytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static async ValueTask WriteStringContentAsync(IBufferWriter<byte> writer, StringContent content, CancellationToken cancellationToken)
    {
        var str = await content.ReadAsStringAsync().ConfigureAwait(false);
        WriteStringAsBytes(writer, str);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static async ValueTask WriteStreamContentAsync(IBufferWriter<byte> writer, StreamContent content, CancellationToken cancellationToken)
    {
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        var buffer = MemoryPoolUtility.RentByteArray(8192);
        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                writer.Write(buffer.AsSpan(0, bytesRead));
            }
        }
        finally
        {
            MemoryPoolUtility.ReturnByteArray(buffer);
        }
    }

    /// <summary>
    /// Fast header parsing with minimal allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool TryParseHeaderLineFast(ReadOnlySpan<byte> line, out string name, out string value)
    {
        name = null;
        value = null;

        var colonIndex = line.IndexOf((byte)':');
        if (colonIndex == -1)
            return false;

        var nameSpan = line[..colonIndex];
        var valueSpan = line[(colonIndex + 1)..];

        // Trim spaces efficiently
        nameSpan = TrimSpaces(nameSpan);
        valueSpan = TrimSpaces(valueSpan);

        if (nameSpan.IsEmpty)
            return false;

        name = MemoryPoolUtility.GetTrimmedStringFromBytes(nameSpan);
        value = MemoryPoolUtility.GetTrimmedStringFromBytes(valueSpan);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<byte> TrimSpaces(ReadOnlySpan<byte> span)
    {
        var start = 0;
        var end = span.Length - 1;

        while (start <= end && span[start] == ' ') start++;
        while (end >= start && span[end] == ' ') end--;

        return start > end ? ReadOnlySpan<byte>.Empty : span.Slice(start, end - start + 1);
    }
}