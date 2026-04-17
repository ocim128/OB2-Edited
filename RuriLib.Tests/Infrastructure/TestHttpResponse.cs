using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace RuriLib.Tests.Infrastructure;

/// <summary>
/// Describes an arbitrary HTTP response that <see cref="TestHttpServer"/> can send back.
/// </summary>
public sealed class TestHttpResponse
{
    /// <summary>
    /// The HTTP status code (default: 200).
    /// </summary>
    public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

    /// <summary>
    /// The reason phrase that accompanies the status code (default: "OK").
    /// When <c>null</c>, the default phrase for <see cref="StatusCode"/> is used.
    /// </summary>
    public string? ReasonPhrase { get; init; }

    /// <summary>
    /// Response headers to include. Header names are emitted verbatim.
    /// For multiple <c>Set-Cookie</c> headers, prefer using
    /// <see cref="SetCookies"/> which emits each value as a separate header line.
    /// </summary>
    public Dictionary<string, string> Headers { get; init; } = new();

    /// <summary>
    /// A list of <c>Set-Cookie</c> values to emit as separate headers.
    /// Each entry becomes its own <c>Set-Cookie</c> header line.
    /// </summary>
    public List<string> SetCookies { get; init; } = new();

    /// <summary>
    /// Additional header lines to append verbatim after <see cref="Headers"/>,
    /// including duplicates that differ only by casing.
    /// </summary>
    public List<(string Name, string Value)> AdditionalHeaders { get; init; } = new();

    /// <summary>
    /// The response body as a string (default: "OK").
    /// </summary>
    public string Body { get; init; } = "OK";

    /// <summary>
    /// The response body as raw bytes. When non-null, takes precedence over <see cref="Body"/>.
    /// </summary>
    public byte[]? RawBody { get; init; }

    /// <summary>
    /// When true, emits the body using HTTP chunked transfer encoding instead of Content-Length.
    /// </summary>
    public bool UseChunkedTransferEncoding { get; init; }

    // -----------------------------------------------------------------
    // Convenience factory helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Creates a simple 200 OK response with the given body.
    /// </summary>
    public static TestHttpResponse Ok(string body = "OK") => new() { Body = body };

    /// <summary>
    /// Creates a redirect (302 Found) response.
    /// The <c>Location</c> header is set automatically.
    /// A response body can be provided (some HTTP stacks read it, others do not).
    /// </summary>
    public static TestHttpResponse Redirect(
        string location,
        string? body = null,
        HttpStatusCode statusCode = HttpStatusCode.Redirect)
    {
        return new TestHttpResponse
        {
            StatusCode = statusCode,
            Headers = new Dictionary<string, string>
            {
                ["Location"] = location
            },
            Body = body ?? string.Empty
        };
    }

    /// <summary>
    /// Creates a response with one or more <c>Set-Cookie</c> headers.
    /// </summary>
    public static TestHttpResponse WithCookies(
        IEnumerable<string> cookies,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string body = "OK")
    {
        return new TestHttpResponse
        {
            StatusCode = statusCode,
            Body = body,
            SetCookies = new List<string>(cookies)
        };
    }

    // -----------------------------------------------------------------
    // Internal serialisation
    // -----------------------------------------------------------------

    internal byte[] Serialize()
    {
        var bodyBytes = RawBody ?? Encoding.UTF8.GetBytes(Body);
        var payloadBytes = UseChunkedTransferEncoding ? SerializeChunkedBody(bodyBytes) : bodyBytes;
        var sb = new StringBuilder();

        var phrase = ReasonPhrase ?? GetDefaultReasonPhrase(StatusCode);
        sb.Append($"HTTP/1.1 {(int)StatusCode} {phrase}\r\n");
        if (UseChunkedTransferEncoding)
        {
            sb.Append("Transfer-Encoding: chunked\r\n");
        }
        else
        {
            sb.Append($"Content-Length: {bodyBytes.Length}\r\n");
        }

        // Standard headers provided by the caller
        foreach (var header in Headers)
        {
            sb.Append($"{header.Key}: {header.Value}\r\n");
        }

        foreach (var header in AdditionalHeaders)
        {
            sb.Append($"{header.Name}: {header.Value}\r\n");
        }

        // Multiple Set-Cookie headers
        foreach (var cookie in SetCookies)
        {
            sb.Append($"Set-Cookie: {cookie}\r\n");
        }

        sb.Append("Connection: close\r\n\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var buffer = new byte[headerBytes.Length + payloadBytes.Length];
        headerBytes.CopyTo(buffer, 0);
        payloadBytes.CopyTo(buffer, headerBytes.Length);
        return buffer;
    }

    private static byte[] SerializeChunkedBody(byte[] bodyBytes)
    {
        using var ms = new MemoryStream();

        if (bodyBytes.Length > 0)
        {
            var midpoint = Math.Max(1, bodyBytes.Length / 2);
            WriteChunk(ms, bodyBytes.AsSpan(0, midpoint));

            if (midpoint < bodyBytes.Length)
            {
                WriteChunk(ms, bodyBytes.AsSpan(midpoint));
            }
        }

        ms.Write("0\r\n\r\n"u8);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> chunk)
    {
        var chunkHeader = Encoding.ASCII.GetBytes($"{chunk.Length:X}\r\n");
        stream.Write(chunkHeader);
        stream.Write(chunk);
        stream.Write("\r\n"u8);
    }

    private static string GetDefaultReasonPhrase(HttpStatusCode code) => code switch
    {
        HttpStatusCode.OK => "OK",
        HttpStatusCode.Created => "Created",
        HttpStatusCode.NoContent => "No Content",
        HttpStatusCode.Moved => "Moved Permanently",
        HttpStatusCode.Redirect => "Found",
        HttpStatusCode.TemporaryRedirect => "Temporary Redirect",
        HttpStatusCode.PermanentRedirect => "Permanent Redirect",
        HttpStatusCode.BadRequest => "Bad Request",
        HttpStatusCode.Unauthorized => "Unauthorized",
        HttpStatusCode.Forbidden => "Forbidden",
        HttpStatusCode.NotFound => "Not Found",
        HttpStatusCode.InternalServerError => "Internal Server Error",
        _ => code.ToString()
    };
}
