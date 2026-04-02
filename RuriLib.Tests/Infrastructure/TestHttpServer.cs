using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RuriLib.Tests.Infrastructure;

internal sealed class TestHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Task _serverTask;

    public Uri Uri { get; }

    private TestHttpServer(TcpListener listener, Task serverTask, Uri uri)
    {
        _listener = listener;
        _serverTask = serverTask;
        Uri = uri;
    }

    public static async Task<TestHttpServer> StartAsync(string responseBody = "OK", int expectedRequests = 1)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = RunServerAsync(listener, responseBody, expectedRequests);
        var uri = new Uri($"http://127.0.0.1:{port}/");

        await Task.Yield();
        return new TestHttpServer(listener, serverTask, uri);
    }

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

    private static async Task RunServerAsync(TcpListener listener, string responseBody, int expectedRequests)
    {
        for (var i = 0; i < expectedRequests; i++)
        {
            using var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            await using var stream = client.GetStream();

            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);

            while (true)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null || line.Length == 0)
                {
                    break;
                }
            }

            var bodyBytes = Encoding.UTF8.GetBytes(responseBody);
            var headerBytes = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n" +
                "Connection: close\r\n\r\n");

            await stream.WriteAsync(headerBytes).ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
    }
}
