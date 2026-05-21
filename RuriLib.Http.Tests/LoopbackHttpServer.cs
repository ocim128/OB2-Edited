using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Http.Tests;

internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<LoopbackHttpRequest, LoopbackHttpResponse> _handler;
    private readonly Task _serverTask;

    public Uri Uri { get; }

    public ConcurrentQueue<LoopbackHttpRequest> Requests { get; } = new();

    private LoopbackHttpServer(
        TcpListener listener,
        Func<LoopbackHttpRequest, LoopbackHttpResponse> handler,
        Uri uri)
    {
        _listener = listener;
        _handler = handler;
        Uri = uri;
        _serverTask = RunAsync();
    }

    public static async Task<LoopbackHttpServer> StartAsync(Func<LoopbackHttpRequest, LoopbackHttpResponse> handler)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await Task.Yield();

        return new LoopbackHttpServer(listener, handler, new Uri($"http://127.0.0.1:{port}/"));
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();

        try
        {
            await _serverTask.ConfigureAwait(false);
        }
        catch
        {
            // Stopping the listener is the normal teardown path.
        }

        _cts.Dispose();
    }

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            await HandleClientAsync(client, _cts.Token).ConfigureAwait(false);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return;
        }

        var parts = requestLine.Split(' ', 3);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line))
            {
                break;
            }

            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                headers[line[..separator]] = line[(separator + 1)..].Trim();
            }
        }

        var request = new LoopbackHttpRequest(
            parts.Length > 0 ? parts[0] : string.Empty,
            parts.Length > 1 ? parts[1] : string.Empty,
            headers);

        Requests.Enqueue(request);

        var response = _handler(request);
        var responseBytes = response.Serialize();
        await stream.WriteAsync(responseBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed record LoopbackHttpRequest(
    string Method,
    string PathAndQuery,
    IReadOnlyDictionary<string, string> Headers);

internal sealed class LoopbackHttpResponse
{
    public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

    public string ReasonPhrase { get; init; } = "OK";

    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public byte[] Body { get; init; } = Encoding.UTF8.GetBytes("OK");

    public byte[] Serialize()
    {
        var headerBuilder = new StringBuilder()
            .Append("HTTP/1.1 ")
            .Append((int)StatusCode)
            .Append(' ')
            .Append(ReasonPhrase)
            .Append("\r\n");

        if (!Headers.ContainsKey("Content-Length"))
        {
            Headers["Content-Length"] = Body.Length.ToString();
        }

        if (!Headers.ContainsKey("Connection"))
        {
            Headers["Connection"] = "close";
        }

        foreach (var header in Headers)
        {
            headerBuilder
                .Append(header.Key)
                .Append(": ")
                .Append(header.Value)
                .Append("\r\n");
        }

        headerBuilder.Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(headerBuilder.ToString());
        var responseBytes = new byte[headerBytes.Length + Body.Length];
        Buffer.BlockCopy(headerBytes, 0, responseBytes, 0, headerBytes.Length);
        Buffer.BlockCopy(Body, 0, responseBytes, headerBytes.Length, Body.Length);
        return responseBytes;
    }
}
