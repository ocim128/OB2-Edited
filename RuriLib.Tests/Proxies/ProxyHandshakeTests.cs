using RuriLib.Proxies;
using RuriLib.Proxies.Clients;
using RuriLib.Proxies.Exceptions;
using System.Net;
using System.Net.Sockets;

namespace RuriLib.Tests.Proxies;

/// <summary>
/// The proxy layer had no test coverage, yet every proxied connection goes through it.
/// These tests drive the clients against a loopback <see cref="ScriptedProxyServer"/> that
/// records the exact handshake bytes, so a malformed SOCKS/HTTP negotiation is caught here
/// instead of against a live proxy.
/// </summary>
public class ProxyHandshakeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task ConnectAsync_RejectsOutOfRangeDestinationPort(int port)
    {
        await using var server = await ScriptedProxyServer.StartAsync([]);
        var client = new Socks5ProxyClient(server.Settings);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ConnectAsync("example.com", port));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ConnectAsync_RejectsMissingDestinationHost(string? host)
    {
        await using var server = await ScriptedProxyServer.StartAsync([]);
        var client = new Socks5ProxyClient(server.Settings);

        await Assert.ThrowsAsync<ArgumentException>(() => client.ConnectAsync(host!, 80));
    }

    [Fact]
    public async Task Socks5_NegotiatesNoAuthThenSendsIpv4ConnectRequest()
    {
        await using var server = await ScriptedProxyServer.StartAsync(new[]
        {
            // Greeting: VER, NMETHODS, METHOD(no auth) -> accept no-auth.
            new ProxyStep(3, [0x05, 0x00]),
            // CONNECT reply: VER, REP(succeeded), RSV, ATYP(ipv4), BND.ADDR, BND.PORT
            new ProxyStep(10, [0x05, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0])
        });

        var client = new Socks5ProxyClient(server.Settings);

        using var tunnel = await client.ConnectAsync("127.0.0.1", 8080);

        var bytes = server.ReceivedBytes;
        Assert.Equal(0x05, bytes[0]);          // version
        Assert.Equal(0x01, bytes[1]);          // one method offered
        Assert.Equal(0x00, bytes[2]);          // no-auth

        Assert.Equal(0x05, bytes[3]);          // version
        Assert.Equal(0x01, bytes[4]);          // CONNECT
        Assert.Equal(0x00, bytes[5]);          // reserved
        Assert.Equal(0x01, bytes[6]);          // IPv4 address type
        Assert.Equal(new byte[] { 127, 0, 0, 1 }, bytes.Skip(7).Take(4));
        Assert.Equal(new byte[] { 0x1F, 0x90 }, bytes.Skip(11).Take(2)); // port 8080 big-endian
    }

    [Fact]
    public async Task Socks5_SendsDomainNameAddressTypeForHostnames()
    {
        await using var server = await ScriptedProxyServer.StartAsync(new[]
        {
            new ProxyStep(3, [0x05, 0x00]),
            new ProxyStep(4 + 1 + 11 + 2, [0x05, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0])
        });

        var client = new Socks5ProxyClient(server.Settings);

        using var tunnel = await client.ConnectAsync("example.com", 80);

        var bytes = server.ReceivedBytes;
        Assert.Equal(0x03, bytes[6]);                                   // domain name type
        Assert.Equal(11, bytes[7]);                                     // length prefix
        Assert.Equal("example.com", System.Text.Encoding.ASCII.GetString(bytes, 8, 11));
    }

    [Fact]
    public async Task Socks5_OffersUsernamePasswordAuthWhenCredentialsPresent()
    {
        await using var server = await ScriptedProxyServer.StartAsync(new[]
        {
            // Reject with 0xff so the client aborts right after the greeting.
            new ProxyStep(3, [0x05, 0xff])
        });

        var settings = server.Settings;
        settings.Credentials = new NetworkCredential("user", "pass");
        var client = new Socks5ProxyClient(settings);

        var exception = await Assert.ThrowsAsync<ProxyException>(() => client.ConnectAsync("127.0.0.1", 80));

        Assert.Contains("not acceptable method", exception.Message);
        Assert.Equal(0x02, server.ReceivedBytes[2]);   // username/password offered
    }

    [Fact]
    public async Task Socks5_ReportsNoAcceptableMethods()
    {
        await using var server = await ScriptedProxyServer.StartAsync(new[]
        {
            new ProxyStep(3, [0x05, 0xff])
        });

        var client = new Socks5ProxyClient(server.Settings);

        var exception = await Assert.ThrowsAsync<ProxyException>(() => client.ConnectAsync("127.0.0.1", 80));

        Assert.Contains("not acceptable method", exception.Message);
    }

    [Fact]
    public async Task Socks5_ReportsConnectFailureStatusFromServer()
    {
        await using var server = await ScriptedProxyServer.StartAsync(new[]
        {
            new ProxyStep(3, [0x05, 0x00]),
            // REP = 0x05 connection refused
            new ProxyStep(10, [0x05, 0x05, 0x00, 0x01, 0, 0, 0, 0, 0, 0])
        });

        var client = new Socks5ProxyClient(server.Settings);

        var exception = await Assert.ThrowsAsync<ProxyException>(() => client.ConnectAsync("127.0.0.1", 80));

        Assert.Contains("Connection refused", exception.Message);
    }

    [Fact]
    public async Task Socks4_SendsIpv4ConnectWithNullTerminatedUserId()
    {
        await using var server = await ScriptedProxyServer.StartAsync(new[]
        {
            // 9 header bytes + "id" + NUL = 11
            new ProxyStep(11, [0x00, 0x5a, 0x00, 0x50, 0, 0, 0, 0])
        });

        var settings = server.Settings;
        settings.Credentials = new NetworkCredential("id", "ignored");
        var client = new Socks4ProxyClient(settings);

        using var tunnel = await client.ConnectAsync("127.0.0.1", 80);

        var expected = new byte[]
        {
            0x04, 0x01,              // VN, CD (CONNECT)
            0x00, 0x50,              // DSTPORT 80
            127, 0, 0, 1,            // DSTIP
            (byte)'i', (byte)'d', 0x00
        };

        Assert.True(expected.SequenceEqual(server.ReceivedBytes), BitConverter.ToString(server.ReceivedBytes));
    }

    [Fact]
    public async Task Socks4_ThrowsProxyExceptionWhenRequestRejected()
    {
        await using var server = await ScriptedProxyServer.StartAsync(new[]
        {
            new ProxyStep(9, [0x00, 0x5b, 0x00, 0x50, 0, 0, 0, 0])
        });

        var client = new Socks4ProxyClient(server.Settings);

        var exception = await Assert.ThrowsAsync<ProxyException>(() => client.ConnectAsync("127.0.0.1", 80));

        Assert.Contains("rejected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HttpProxy_SendsConnectWithProxyAuthorization()
    {
        await using var server = await ScriptedProxyServer.StartAsync(new[]
        {
            new ProxyStep(1, System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n"))
        }, readUntilQuiet: true);

        var settings = server.Settings;
        settings.Credentials = new NetworkCredential("user", "pass");
        var client = new HttpProxyClient(settings);

        using var tunnel = await client.ConnectAsync("example.com", 443);

        var request = System.Text.Encoding.ASCII.GetString(server.ReceivedBytes);

        Assert.StartsWith("CONNECT example.com:443 HTTP/1.1\r\n", request);
        Assert.Contains("Proxy-Authorization: Basic dXNlcjpwYXNz", request);   // user:pass
    }

    [Fact]
    public async Task HttpProxy_OmitsAuthorizationHeaderWithoutCredentials()
    {
        await using var server = await ScriptedProxyServer.StartAsync(new[]
        {
            new ProxyStep(1, System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n"))
        }, readUntilQuiet: true);

        var client = new HttpProxyClient(server.Settings);

        using var tunnel = await client.ConnectAsync("example.com", 443);

        var request = System.Text.Encoding.ASCII.GetString(server.ReceivedBytes);

        Assert.DoesNotContain("Proxy-Authorization", request);
    }

    [Fact]
    public async Task HttpProxy_ThrowsWhenProxyDoesNotReplyOk()
    {
        await using var server = await ScriptedProxyServer.StartAsync(new[]
        {
            new ProxyStep(1, System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 407 Proxy Authentication Required\r\n\r\n"))
        }, readUntilQuiet: true);

        var client = new HttpProxyClient(server.Settings);

        var exception = await Assert.ThrowsAsync<ProxyException>(() => client.ConnectAsync("example.com", 443));

        Assert.Contains("200 OK", exception.Message);
    }

    private sealed record ProxyStep(int ExpectedBytes, byte[] Response);

    /// <summary>
    /// A loopback TCP server that plays a fixed script of request/response steps and records
    /// everything the client sent. The connection is always closed at the end of the script,
    /// so no client read can block forever.
    /// </summary>
    private sealed class ScriptedProxyServer : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource cts = new();
        private readonly List<byte> received = [];
        private readonly Task acceptTask;

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public ProxySettings Settings => new()
        {
            Host = "127.0.0.1",
            Port = Port,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ReadWriteTimeOut = TimeSpan.FromSeconds(5)
        };

        public byte[] ReceivedBytes
        {
            get
            {
                lock (received)
                {
                    return received.ToArray();
                }
            }
        }

        private ScriptedProxyServer(
            TcpListener listener,
            IReadOnlyList<ProxyStep> script,
            bool readUntilQuiet)
        {
            this.listener = listener;
            acceptTask = Task.Run(() => AcceptAsync(script, readUntilQuiet));
        }

        public static Task<ScriptedProxyServer> StartAsync(
            IReadOnlyList<ProxyStep> script,
            bool readUntilQuiet = false)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new ScriptedProxyServer(listener, script, readUntilQuiet));
        }

        private async Task AcceptAsync(IReadOnlyList<ProxyStep> script, bool readUntilQuiet)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false);
                using var stream = client.GetStream();

                foreach (var step in script)
                {
                    if (readUntilQuiet)
                    {
                        await ReadUntilQuietAsync(stream).ConfigureAwait(false);
                    }
                    else
                    {
                        await ReadExactAsync(stream, step.ExpectedBytes).ConfigureAwait(false);
                    }

                    await stream.WriteAsync(step.Response, cts.Token).ConfigureAwait(false);
                    await stream.FlushAsync(cts.Token).ConfigureAwait(false);
                }

                // Give the client time to drain the final response before the socket closes.
                await Task.Delay(100, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException)
            {
            }
        }

        private async Task ReadExactAsync(NetworkStream stream, int count)
        {
            var buffer = new byte[count];
            var total = 0;

            // Bounded: if the client sends fewer bytes than the script expects, give up
            // instead of blocking the whole test run.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                while (total < count)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(total, count - total), timeout.Token)
                        .ConfigureAwait(false);

                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                }
            }
            catch (OperationCanceledException)
            {
            }

            if (total == 0)
            {
                return;
            }

            lock (received)
            {
                received.AddRange(buffer.Take(total));
            }
        }

        private async Task ReadUntilQuietAsync(NetworkStream stream)
        {
            var buffer = new byte[1024];

            while (true)
            {
                using var quiet = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                quiet.CancelAfter(TimeSpan.FromMilliseconds(250));

                int read;
                try
                {
                    read = await stream.ReadAsync(buffer, quiet.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cts.IsCancellationRequested)
                {
                    return;   // quiet period elapsed: the request is complete
                }

                if (read == 0)
                {
                    return;
                }

                lock (received)
                {
                    received.AddRange(buffer.Take(read));
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await cts.CancelAsync().ConfigureAwait(false);
            listener.Stop();

            // Never let teardown hang the suite if the client never connected.
            await Task.WhenAny(acceptTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

            cts.Dispose();
        }
    }
}
