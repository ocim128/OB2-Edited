using RuriLib.Proxies;
using RuriLib.Proxies.Clients;
using RuriLib.Proxies.Exceptions;
using System.Net;
using System.Net.Sockets;

namespace RuriLib.Tests.Proxies;

/// <summary>
/// Timeout handling for <see cref="HttpProxyClient"/>. The HTTP CONNECT handshake polls
/// <c>NetworkStream.ReadTimeout</c>, so a "disabled timeout" setting (0 / infinite) must not turn
/// into an immediate timeout for proxies that take a moment to reply.
/// </summary>
public class HttpProxyTimeoutTests
{
    [Fact]
    public async Task HttpProxy_WithDisabledSocketTimeout_StillWaitsForSlowProxyReply()
    {
        await using var server = await DelayedProxyServer.StartAsync(
            delayBeforeReply: TimeSpan.FromMilliseconds(400),
            reply: "HTTP/1.1 200 Connection established\r\n\r\n");

        var client = new HttpProxyClient(new ProxySettings
        {
            Host = "127.0.0.1",
            Port = server.Port,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ReadWriteTimeOut = TimeSpan.Zero   // user asked for "no timeout"
        });

        // A slow-but-successful proxy must not be reported as a timeout.
        using var tunnel = await client.ConnectAsync("example.com", 443);

        Assert.True(server.WasRead);
    }

    [Fact]
    public async Task HttpProxy_WithFiniteSocketTimeout_TimesOutWhenProxyNeverReplies()
    {
        await using var server = await DelayedProxyServer.StartAsync(
            delayBeforeReply: TimeSpan.FromSeconds(5),
            reply: "HTTP/1.1 200 Connection established\r\n\r\n");

        var client = new HttpProxyClient(new ProxySettings
        {
            Host = "127.0.0.1",
            Port = server.Port,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ReadWriteTimeOut = TimeSpan.FromMilliseconds(300)
        });

        var exception = await Assert.ThrowsAsync<ProxyException>(() => client.ConnectAsync("example.com", 443));

        Assert.Contains("Timed out", exception.Message);
    }

    private sealed class DelayedProxyServer : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource cts = new();
        private readonly Task acceptTask;

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public bool WasRead { get; private set; }

        private DelayedProxyServer(TcpListener listener, TimeSpan delayBeforeReply, byte[] reply)
        {
            this.listener = listener;
            acceptTask = Task.Run(() => AcceptAsync(delayBeforeReply, reply));
        }

        public static Task<DelayedProxyServer> StartAsync(TimeSpan delayBeforeReply, string reply)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new DelayedProxyServer(listener, delayBeforeReply, System.Text.Encoding.ASCII.GetBytes(reply)));
        }

        private async Task AcceptAsync(TimeSpan delayBeforeReply, byte[] reply)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false);
                using var stream = client.GetStream();

                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    var buffer = new byte[1024];
                    var read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                    WasRead = read > 0;
                }

                await Task.Delay(delayBeforeReply, cts.Token).ConfigureAwait(false);
                await stream.WriteAsync(reply, cts.Token).ConfigureAwait(false);
                await stream.FlushAsync(cts.Token).ConfigureAwait(false);

                await Task.Delay(200, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await cts.CancelAsync().ConfigureAwait(false);
            listener.Stop();
            await Task.WhenAny(acceptTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            cts.Dispose();
        }
    }
}
