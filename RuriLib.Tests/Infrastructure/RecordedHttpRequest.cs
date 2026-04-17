using System;
using System.Collections.Generic;

namespace RuriLib.Tests.Infrastructure;

/// <summary>
/// Captures the details of an HTTP request received by <see cref="TestHttpServer"/>.
/// </summary>
public sealed class RecordedHttpRequest
{
    /// <summary>
    /// The raw first line of the HTTP request (e.g. "GET /path HTTP/1.1").
    /// When the client sends an absolute URI (common with proxies), this will
    /// contain the full URI rather than just the path component.
    /// </summary>
    public string FirstLine { get; init; } = string.Empty;

    /// <summary>
    /// The HTTP method extracted from the first line (e.g. "GET", "POST").
    /// </summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>
    /// The request path (and query string) extracted from the first line.
    /// If the first line contained an absolute URI, this is the path component
    /// of that URI.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// When the first line of the request contains an absolute URI
    /// (e.g. "GET http://host/path HTTP/1.1"), this property holds that
    /// full absolute URI string. Otherwise it is <c>null</c>.
    /// </summary>
    public string? AbsoluteUriInFirstLine { get; init; }

    /// <summary>
    /// Request headers keyed by header name (case-insensitive lookup,
    /// original casing preserved).
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All headers in order, including duplicates.
    /// </summary>
    public IReadOnlyList<(string Name, string Value)> AllHeaders { get; init; } = Array.Empty<(string, string)>();

    /// <summary>
    /// The decoded request body, or <c>null</c> when no body was sent.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// The raw request body bytes, or an empty array when no body was sent.
    /// </summary>
    public byte[] RawBody { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Checks whether the request has a header with the specified name.
    /// </summary>
    public bool HasHeader(string name) =>
        Headers.ContainsKey(name);

    /// <summary>
    /// Gets the value of a header by name, or <c>null</c> if not present.
    /// </summary>
    public string? GetHeader(string name) =>
        Headers.TryGetValue(name, out var value) ? value : null;
}
