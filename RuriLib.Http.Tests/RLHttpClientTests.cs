using RuriLib.Http.Models;
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

public class RLHttpClientTests
{
    [Fact]
    public async Task SendAsync_ForwardsHeadersAndQuery()
    {
        var userAgent = "Flux-Test";
        await using var server = await LoopbackHttpServer.StartAsync(_ => new LoopbackHttpResponse());

        var response = await RequestAsync(new HttpRequest
        {
            Method = HttpMethod.Get,
            Uri = new Uri(server.Uri, "/get?key=value"),
            Headers = { ["User-Agent"] = userAgent }
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

        var response = await RequestAsync(new HttpRequest
        {
            Method = HttpMethod.Get,
            Uri = server.Uri
        });

        Assert.Equal(body.Length, response.Content.Headers.ContentLength);
        Assert.Equal("text/html", response.Content.Headers.ContentType.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        Assert.Equal("<html><body>ok</body></html>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SendAsync_AppliesResponseCookiesToRequestCookieDictionary()
    {
        var cookies = new System.Collections.Generic.Dictionary<string, string>();
        await using var server = await LoopbackHttpServer.StartAsync(_ =>
        {
            var response = new LoopbackHttpResponse();
            response.Headers["Set-Cookie"] = "name=value; Path=/";
            return response;
        });

        using var client = new RLHttpClient(new NoProxyClient(new ProxySettings()));
        using var response = await client.SendAsync(new HttpRequest
        {
            Method = HttpMethod.Get,
            Uri = server.Uri,
            Cookies = cookies
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(cookies);
        Assert.Equal("value", cookies["name"]);
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

        var response = await RequestAsync(new HttpRequest
        {
            Method = HttpMethod.Get,
            Uri = server.Uri
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_UsesExplicitHostHeader()
    {
        await using var server = await LoopbackHttpServer.StartAsync(_ => new LoopbackHttpResponse());

        var response = await RequestAsync(new HttpRequest
        {
            Method = HttpMethod.Get,
            Uri = server.Uri,
            Headers = { ["Host"] = "example.test" }
        });

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

        var response = await RequestAsync(new HttpRequest
        {
            Method = HttpMethod.Get,
            Uri = server.Uri,
            Headers = { ["Accept-Encoding"] = "gzip" }
        });

        Assert.Equal("compressed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SendAsync_DecompressesBrotli()
    {
        var body = Compress("compressed", stream => new BrotliStream(stream, CompressionLevel.Fastest, leaveOpen: true));
        await using var server = await LoopbackHttpServer.StartAsync(_ =>
        {
            var response = new LoopbackHttpResponse { Body = body };
            response.Headers["Content-Encoding"] = "br";
            return response;
        });

        var response = await RequestAsync(new HttpRequest
        {
            Method = HttpMethod.Get,
            Uri = server.Uri,
            Headers = { ["Accept-Encoding"] = "br" }
        });

        Assert.Equal("compressed", await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponse> RequestAsync(HttpRequest request)
    {
        using var client = new RLHttpClient(new NoProxyClient(new ProxySettings()));
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
