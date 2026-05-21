using RuriLib.Proxies;
using RuriLib.Proxies.Clients;
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace RuriLib.Http.Tests;

public class ProxyClientHandlerTests
{
    [Fact]
    public async Task SendAsync_ForwardsHeadersAndQuery()
    {
        var userAgent = "Flux-Test";
        await using var server = await LoopbackHttpServer.StartAsync(_ => new LoopbackHttpResponse());

        using var response = await RequestAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(server.Uri, "/get?key=value"))
        {
            Headers = { { "User-Agent", userAgent } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(server.Requests.TryPeek(out var request));
        Assert.Equal("/get?key=value", request.PathAndQuery);
        Assert.Equal(userAgent, request.Headers["User-Agent"]);
    }

    [Fact]
    public async Task SendAsync_ReadsContentHeadersAndBody()
    {
        var body = Encoding.UTF8.GetBytes("<html><body>ok</body></html>");
        await using var server = await LoopbackHttpServer.StartAsync(_ =>
        {
            var response = new LoopbackHttpResponse { Body = body };
            response.Headers["Content-Type"] = "text/html; charset=utf-8";
            return response;
        });

        using var response = await RequestAsync(new HttpRequestMessage(HttpMethod.Get, server.Uri));

        Assert.Equal(body.Length, response.Content.Headers.ContentLength);
        Assert.Equal("text/html", response.Content.Headers.ContentType.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        Assert.Equal("<html><body>ok</body></html>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SendAsync_StoresResponseCookies()
    {
        await using var server = await LoopbackHttpServer.StartAsync(_ =>
        {
            var response = new LoopbackHttpResponse();
            response.Headers["Set-Cookie"] = "name=value; Path=/";
            return response;
        });

        var cookieContainer = new CookieContainer();
        using var handler = new ProxyClientHandler(new NoProxyClient(new ProxySettings()))
        {
            CookieContainer = cookieContainer
        };

        using var client = new HttpClient(handler);
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, server.Uri));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookies = cookieContainer.GetCookies(server.Uri);
        Assert.Single(cookies);
        Assert.Equal("value", cookies["name"].Value);
    }

    [Fact]
    public async Task SendAsync_ReturnsStatusCode()
    {
        await using var server = await LoopbackHttpServer.StartAsync(_ =>
            new LoopbackHttpResponse
            {
                StatusCode = HttpStatusCode.NotFound,
                ReasonPhrase = "Not Found",
                Body = Array.Empty<byte>()
            });

        using var response = await RequestAsync(new HttpRequestMessage(HttpMethod.Get, server.Uri));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_UsesExplicitHostHeader()
    {
        await using var server = await LoopbackHttpServer.StartAsync(_ => new LoopbackHttpResponse());
        using var message = new HttpRequestMessage(HttpMethod.Get, server.Uri);
        message.Headers.Host = "example.test";

        using var response = await RequestAsync(message);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(server.Requests.TryPeek(out var request));
        Assert.Equal("example.test", request.Headers["Host"]);
    }

    [Fact]
    public async Task SendAsync_DecompressesGzip()
    {
        var body = Compress("compressed", stream => new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true));
        await using var server = await LoopbackHttpServer.StartAsync(_ =>
        {
            var response = new LoopbackHttpResponse { Body = body };
            response.Headers["Content-Encoding"] = "gzip";
            return response;
        });

        using var message = new HttpRequestMessage(HttpMethod.Get, server.Uri);
        message.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");

        using var response = await RequestAsync(message);

        Assert.Equal("compressed", await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> RequestAsync(HttpRequestMessage request)
    {
        using var handler = new ProxyClientHandler(new NoProxyClient(new ProxySettings()))
        {
            CookieContainer = new CookieContainer()
        };

        using var client = new HttpClient(handler);
        return await client.SendAsync(request);
    }

    private static byte[] Compress(string value, Func<Stream, Stream> createCompressionStream)
    {
        using var output = new MemoryStream();
        using (var compressionStream = createCompressionStream(output))
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            compressionStream.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }
}
