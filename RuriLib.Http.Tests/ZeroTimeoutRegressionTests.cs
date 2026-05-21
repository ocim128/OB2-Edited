using RuriLib.Http.Models;
using RuriLib.Proxies;
using RuriLib.Proxies.Clients;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace RuriLib.Http.Tests;

public class ZeroTimeoutRegressionTests
{
    [Fact]
    public async Task RLHttpClient_SendAsync_AllowsZeroProxyTimeouts()
    {
        await using var server = await LoopbackHttpServer.StartAsync(_ => new LoopbackHttpResponse());

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
        await using var server = await LoopbackHttpServer.StartAsync(_ => new LoopbackHttpResponse());

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
}
