using RuriLib.Functions.Http;
using RuriLib.Models.Proxies;
using RuriLib.Proxies;
using RuriLib.Proxies.Clients;
using System;
using System.Net;
using System.Reflection;
using System.Security.Authentication;

namespace RuriLib.Tests.Functions.Http;

public class HttpTransportConfigurationTests
{
    [Fact]
    public void RLHttpClient_GetPoolKey_SeparatesProxyTypeCredentialsAndTimeouts()
    {
        var httpClientA = CreateRlHttpClient(
            new HttpProxyClient(new ProxySettings
            {
                Host = "proxy.local",
                Port = 8080,
                Credentials = new NetworkCredential("user-a", "pass-a"),
                ConnectTimeout = TimeSpan.FromSeconds(5),
                ReadWriteTimeOut = TimeSpan.FromSeconds(10)
            }));
        var httpClientB = CreateRlHttpClient(
            new HttpProxyClient(new ProxySettings
            {
                Host = "proxy.local",
                Port = 8080,
                Credentials = new NetworkCredential("user-b", "pass-b"),
                ConnectTimeout = TimeSpan.FromSeconds(5),
                ReadWriteTimeOut = TimeSpan.FromSeconds(10)
            }));
        var socksClient = CreateRlHttpClient(
            new Socks5ProxyClient(new ProxySettings
            {
                Host = "proxy.local",
                Port = 8080,
                Credentials = new NetworkCredential("user-a", "pass-a"),
                ConnectTimeout = TimeSpan.FromSeconds(5),
                ReadWriteTimeOut = TimeSpan.FromSeconds(10)
            }));
        var timeoutClient = CreateRlHttpClient(
            new HttpProxyClient(new ProxySettings
            {
                Host = "proxy.local",
                Port = 8080,
                Credentials = new NetworkCredential("user-a", "pass-a"),
                ConnectTimeout = TimeSpan.FromSeconds(15),
                ReadWriteTimeOut = TimeSpan.FromSeconds(30)
            }));

        var keyA = InvokePoolKey(httpClientA);
        var keyB = InvokePoolKey(httpClientB);
        var keySocks = InvokePoolKey(socksClient);
        var keyTimeout = InvokePoolKey(timeoutClient);

        Assert.NotEqual(keyA, keyB);
        Assert.NotEqual(keyA, keySocks);
        Assert.NotEqual(keyA, keyTimeout);
    }

    [Fact]
    public void RLHttpClientRequestHandler_GenerateClientKey_SeparatesTimeouts()
    {
        using var context = new HttpTransportTestContext();
        context.Data.UseProxy = true;
        context.Data.Proxy = new Proxy("proxy.local", 8080, ProxyType.Http, "user", "pass");

        var keyMethod = typeof(RuriLib.Blocks.Requests.Http.Methods).Assembly
            .GetType("RuriLib.Functions.Http.RLHttpClientRequestHandler", throwOnError: true)!
            .GetMethod("GenerateClientKey", BindingFlags.NonPublic | BindingFlags.Static)!;

        var shortTimeouts = new HttpOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ReadWriteTimeout = TimeSpan.FromSeconds(10)
        };
        var longTimeouts = new HttpOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            ReadWriteTimeout = TimeSpan.FromSeconds(30)
        };

        var shortKey = (string)keyMethod.Invoke(null, new object[] { context.Data, shortTimeouts })!;
        var longKey = (string)keyMethod.Invoke(null, new object[] { context.Data, longTimeouts })!;

        Assert.NotEqual(shortKey, longKey);
    }

    private static RuriLib.Http.RLHttpClient CreateRlHttpClient(ProxyClient proxyClient)
        => new(proxyClient)
        {
            SslProtocols = SslProtocols.Tls12,
            CertRevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck
        };

    private static string InvokePoolKey(RuriLib.Http.RLHttpClient client)
    {
        var method = typeof(RuriLib.Http.RLHttpClient)
            .GetMethod("GetPoolKey", BindingFlags.NonPublic | BindingFlags.Instance)!;

        return (string)method.Invoke(client, new object[] { "example.com", 443, true })!;
    }
}
