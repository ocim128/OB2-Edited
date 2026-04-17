using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RuriLib.Tests.Infrastructure;

/// <summary>
/// A lightweight, programmable HTTP server for use in unit tests.
/// Supports per-request handlers, request recording, arbitrary responses,
/// redirects with bodies, multiple <c>Set-Cookie</c> headers, and
/// <see cref="RecordedHttpRequest.AbsoluteUriInFirstLine"/> capture.
/// </summary>
internal sealed class TestHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Task _serverTask;
    private readonly ConcurrentQueue<RecordedHttpRequest> _recordedRequests;

    /// <summary>
    /// The base URI that clients should use to reach this server.
    /// </summary>
    public Uri Uri { get; }

    /// <summary>
    /// All requests received by this server, in order.
    /// Thread-safe for concurrent reads from test assertions.
    /// </summary>
    public IReadOnlyList<RecordedHttpRequest> RecordedRequests => _recordedRequests.ToArray();

    /// <summary>
    /// The well-known header name used for Set-Cookie entries in
    /// <see cref="TestHttpResponse.Headers"/>.
    /// Prefer using <see cref="TestHttpResponse.SetCookies"/> instead.
    /// </summary>
    public const string SetCookieHeaderName = "Set-Cookie";

    private TestHttpServer(
        TcpListener listener,
        Task serverTask,
        Uri uri,
        ConcurrentQueue<RecordedHttpRequest> recordedRequests)
    {
        _listener = listener;
        _serverTask = serverTask;
        Uri = uri;
        _recordedRequests = recordedRequests;
    }

    // -----------------------------------------------------------------
    // Static factory — legacy signature (backward compatible)
    // -----------------------------------------------------------------

    /// <summary>
    /// Starts a server that returns 200 OK with <paramref name="responseBody"/>
    /// for every request. This overload preserves backward compatibility with
    /// the original <c>TestHttpServer</c>.
    /// </summary>
    public static async Task<TestHttpServer> StartAsync(
        string responseBody = "OK",
        int expectedRequests = 1)
    {
        var scenario = HttpTestScenario.AlwaysOk(responseBody);
        return await StartAsync(scenario, expectedRequests).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Static factory — scenario-based
    // -----------------------------------------------------------------

    /// <summary>
    /// Starts a server driven by the given <see cref="HttpTestScenario"/>.
    /// Captured requests are available via <see cref="RecordedRequests"/>.
    /// </summary>
    /// <param name="scenario">The scenario describing how to respond to requests.</param>
    /// <param name="expectedRequests">
    /// The number of requests the server will accept before shutting down its
    /// accept loop. The <see cref="DisposeAsync"/> method stops the listener
    /// regardless, so this is only an upper bound.
    /// </param>
    public static async Task<TestHttpServer> StartAsync(
        HttpTestScenario scenario,
        int expectedRequests = 1)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var bag = new ConcurrentQueue<RecordedHttpRequest>();
        var serverTask = RunServerAsync(listener, scenario, expectedRequests, bag);
        var uri = new Uri($"http://127.0.0.1:{port}/");

        await Task.Yield();

        return new TestHttpServer(listener, serverTask, uri, bag);
    }

    // -----------------------------------------------------------------
    // IAsyncDisposable
    // -----------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();

        try
        {
            await _serverTask.ConfigureAwait(false);
        }
        catch
        {
            // The listener is intentionally stopped during teardown.
        }
    }

    // -----------------------------------------------------------------
    // Core server loop
    // -----------------------------------------------------------------

    private static async Task RunServerAsync(
        TcpListener listener,
        HttpTestScenario scenario,
        int expectedRequests,
        ConcurrentQueue<RecordedHttpRequest> recordedQueue)
    {
        for (var i = 0; i < expectedRequests; i++)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (SocketException) when (!listener.Server.IsBound)
            {
                // Listener was stopped (teardown).
                break;
            }

            using (client)
            await using (var stream = client.GetStream())
            {
                var recorded = await ReadRequestAsync(stream).ConfigureAwait(false);
                recordedQueue.Enqueue(recorded);

                var response = ResolveResponse(scenario, recorded, i);
                var responseBytes = response.Serialize();

                await stream.WriteAsync(responseBytes).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
        }
    }

    // -----------------------------------------------------------------
    // Request parsing
    // -----------------------------------------------------------------

    private static async Task<RecordedHttpRequest> ReadRequestAsync(NetworkStream stream)
    {
        var headerBuffer = new byte[8192];
        var headerBytesRead = 0;
        var headerEndIndex = -1;

        // Read until we find \r\n\r\n or fill the buffer
        while (headerBytesRead < headerBuffer.Length)
        {
            var read = await stream.ReadAsync(
                headerBuffer.AsMemory(headerBytesRead, headerBuffer.Length - headerBytesRead)).ConfigureAwait(false);
            if (read == 0) break;
            headerBytesRead += read;

            // Scan for \r\n\r\n
            for (var i = 3; i < headerBytesRead; i++)
            {
                if (headerBuffer[i - 3] == '\r' && headerBuffer[i - 2] == '\n' &&
                    headerBuffer[i - 1] == '\r' && headerBuffer[i] == '\n')
                {
                    headerEndIndex = i;
                    goto HeaderComplete;
                }
            }
        }

    HeaderComplete:
        if (headerEndIndex < 0)
        {
            // Malformed request — return what we have
            return new RecordedHttpRequest { FirstLine = string.Empty, Method = "UNKNOWN", Path = "/" };
        }

        var headerText = Encoding.ASCII.GetString(headerBuffer, 0, headerEndIndex - 3);
        var lines = headerText.Split("\r\n");

        var firstLine = lines.Length > 0 ? lines[0] : string.Empty;
        var (method, path, absoluteUri) = ParseFirstLine(firstLine);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allHeaders = new List<(string Name, string Value)>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex >= 0)
            {
                var name = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                headers[name] = value;
                allHeaders.Add((name, value));
            }
        }

        // Body: bytes after the \r\n\r\n delimiter that were already read
        var bodyStart = headerEndIndex + 1; // first byte after the final \n
        var preambleLength = headerBytesRead - bodyStart;
        byte[]? bodyBytes = null;

        if (headers.TryGetValue("Content-Length", out var contentLengthStr)
            && int.TryParse(contentLengthStr, out var contentLength)
            && contentLength > 0)
        {
            bodyBytes = new byte[contentLength];
            // Copy preamble bytes
            if (preambleLength > 0)
            {
                var copyCount = Math.Min(preambleLength, contentLength);
                Array.Copy(headerBuffer, bodyStart, bodyBytes, 0, copyCount);
            }
            // Read remaining
            var remaining = contentLength - preambleLength;
            var offset = preambleLength;
            while (remaining > 0)
            {
                var read = await stream.ReadAsync(
                    bodyBytes.AsMemory(offset, remaining)).ConfigureAwait(false);
                if (read == 0) break;
                offset += read;
                remaining -= read;
            }
        }

        string? body = bodyBytes != null ? Encoding.UTF8.GetString(bodyBytes) : null;

        return new RecordedHttpRequest
        {
            FirstLine = firstLine,
            Method = method,
            Path = path,
            AbsoluteUriInFirstLine = absoluteUri,
            Headers = headers,
            AllHeaders = allHeaders,
            Body = body,
            RawBody = bodyBytes ?? Array.Empty<byte>()
        };
    }

    /// <summary>
    /// Parses the first line of an HTTP request.
    /// Returns (method, path, absoluteUriOrNull).
    /// </summary>
    private static (string method, string path, string? absoluteUri) ParseFirstLine(string firstLine)
    {
        // Typical: "GET /path HTTP/1.1"
        // Proxy-style: "GET http://host/path HTTP/1.1"
        var space1 = firstLine.IndexOf(' ');
        if (space1 < 0) return (firstLine, "/", null);

        var method = firstLine[..space1];
        var rest = firstLine[(space1 + 1)..];
        var space2 = rest.IndexOf(' ');
        var target = space2 >= 0 ? rest[..space2] : rest;

        string? absoluteUri = null;
        string path;

        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            absoluteUri = target;
            // Extract the path portion from the absolute URI
            try
            {
                var uri = new Uri(target);
                path = uri.PathAndQuery;
            }
            catch
            {
                path = target;
            }
        }
        else
        {
            path = target;
        }

        return (method, path, absoluteUri);
    }

    // -----------------------------------------------------------------
    // Response resolution
    // -----------------------------------------------------------------

    private static TestHttpResponse ResolveResponse(
        HttpTestScenario scenario,
        RecordedHttpRequest request,
        int index)
    {
        // Per-request handler takes precedence if it returns non-null.
        if (scenario.RequestHandler is not null)
        {
            var handlerResponse = scenario.RequestHandler(request, index);
            if (handlerResponse is not null)
                return handlerResponse;
        }

        // Fall back to the static response list.
        var responses = scenario.Responses;
        if (responses.Count == 0)
            return TestHttpResponse.Ok();

        // Reuse the last response if the request count exceeds the list length.
        var resolvedIndex = Math.Min(index, responses.Count - 1);
        return responses[resolvedIndex];
    }
}
