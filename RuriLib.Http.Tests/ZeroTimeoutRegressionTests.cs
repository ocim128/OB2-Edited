using RuriLib.Http.Models;
using RuriLib.Proxies;
using RuriLib.Proxies.Clients;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace RuriLib.Http.Tests;

public class ZeroTimeoutRegressionTests
{
    [Fact]
    public async Task RLHttpClient_SendAsync_AllowsZeroProxyTimeouts()
    {
        await using var server = await TestHttpServer.StartAsync();

        var settings = new ProxySettings
        {
            ConnectTimeout = TimeSpan.Zero,
            ReadWriteTimeOut = TimeSpan.Zero
        };

        var request = new HttpRequest
        {
            Method = HttpMethod.Get,
            Uri = server.Uri
        };

        using var client = new RLHttpClient(new NoProxyClient(settings));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProxyClientHandler_SendAsync_AllowsZeroProxyTimeouts()
    {
        await using var server = await TestHttpServer.StartAsync();

        var settings = new ProxySettings
        {
            ConnectTimeout = TimeSpan.Zero,
            ReadWriteTimeOut = TimeSpan.Zero
        };

        using var handler = new ProxyClientHandler(new NoProxyClient(settings))
        {
            CookieContainer = new CookieContainer()
        };

        using var client = new HttpClient(handler);
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, server.Uri));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", await response.Content.ReadAsStringAsync());
    }

    private sealed class TestHttpServer : IAsyncDisposable
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

        public static async Task<TestHttpServer> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serverTask = RunServerAsync(listener);
            var uri = new Uri($"http://127.0.0.1:{port}/");

            await Task.Yield();
            return new TestHttpServer(listener, serverTask, uri);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();

            try
            {
                await _serverTask;
            }
            catch
            {
                // The listener is intentionally stopped during teardown.
            }
        }

        private static async Task RunServerAsync(TcpListener listener)
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line == null || line.Length == 0)
                {
                    break;
                }
            }

            var responseBytes = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Length: 2\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n" +
                "Connection: close\r\n\r\n" +
                "OK");

            await stream.WriteAsync(responseBytes);
            await stream.FlushAsync();
        }
    }
}
