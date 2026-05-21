using RuriLib.Functions.Http.Options;
using RuriLib.Tests.Infrastructure;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RuriLib.Tests.Functions.Http;

public class HttpRuntimeHardeningTests
{
    [Fact]
    public async Task RuriLibHttp_KeepAlive_ReusesDelimitedConnection()
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(
                new TestHttpResponse { Body = "first", CloseConnection = false },
                new TestHttpResponse { Body = "second", CloseConnection = false }),
            expectedRequests: 2);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(context.Data, HttpLibrary.RuriLibHttp, server.Uri);
        var firstResponseHeaders = string.Join("; ", context.Data.HEADERS.Select(h => $"{h.Key}={h.Value}"));
        await HttpTransportTestHelper.SendStandardAsync(context.Data, HttpLibrary.RuriLibHttp, server.Uri);

        Assert.Equal(2, server.RecordedRequests.Count);
        Assert.True(
            server.AcceptedConnections == 1,
            $"Expected one accepted connection, got {server.AcceptedConnections}. " +
            $"First response headers: {firstResponseHeaders}. " +
            $"Requests: {string.Join(" | ", server.RecordedRequests.Select(r => $"{r.FirstLine} [{string.Join("; ", r.AllHeaders.Select(h => $"{h.Name}={h.Value}"))}]"))}");
    }

    [Fact]
    public async Task RuriLibHttp_ConnectionClose_DoesNotReuseClosedConnection()
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(
                new TestHttpResponse { Body = "first" },
                new TestHttpResponse { Body = "second" }),
            expectedRequests: 2);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(context.Data, HttpLibrary.RuriLibHttp, server.Uri);
        await HttpTransportTestHelper.SendStandardAsync(context.Data, HttpLibrary.RuriLibHttp, server.Uri);

        Assert.Equal(2, server.RecordedRequests.Count);
        Assert.Equal(2, server.AcceptedConnections);
    }

    [Fact]
    public async Task RuriLibHttp_NoBodyStatus_DoesNotWaitForConnectionClose()
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(
                new TestHttpResponse
                {
                    StatusCode = System.Net.HttpStatusCode.NoContent,
                    Body = string.Empty,
                    OmitContentLength = true,
                    CloseConnection = false
                },
                new TestHttpResponse { Body = "second", CloseConnection = false }),
            expectedRequests: 2);
        using var context = new HttpTransportTestContext();
        context.Settings.RuriLibSettings.ProxySettings.ProxyReadWriteTimeoutMilliseconds = 250;

        await HttpTransportTestHelper.SendStandardAsync(context.Data, HttpLibrary.RuriLibHttp, server.Uri);
        await HttpTransportTestHelper.SendStandardAsync(context.Data, HttpLibrary.RuriLibHttp, server.Uri);

        Assert.Equal(2, server.RecordedRequests.Count);
        Assert.Equal(1, server.AcceptedConnections);
    }

    [Fact]
    public async Task SystemNet_ReusesSharedClientConnectionForEquivalentRequests()
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(
                new TestHttpResponse { Body = "first", CloseConnection = false },
                new TestHttpResponse { Body = "second", CloseConnection = false }),
            expectedRequests: 2);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(context.Data, HttpLibrary.SystemNet, server.Uri);
        await HttpTransportTestHelper.SendStandardAsync(context.Data, HttpLibrary.SystemNet, server.Uri);

        Assert.Equal(2, server.RecordedRequests.Count);
        Assert.Equal(1, server.AcceptedConnections);
    }

    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task DisableCookieParsing_DoesNotPersistResponseCookies(HttpLibrary library)
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(
                TestHttpResponse.WithCookies(new[] { "session=secret-cookie; Path=/" }),
                TestHttpResponse.Ok("second")),
            expectedRequests: 2);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(
            context.Data,
            library,
            server.Uri,
            autoRedirect: false,
            maxRedirects: 0,
            disableCookieParsing: true);
        await HttpTransportTestHelper.SendStandardAsync(
            context.Data,
            library,
            server.Uri,
            autoRedirect: false,
            maxRedirects: 0,
            disableCookieParsing: true);

        Assert.Equal(2, server.RecordedRequests.Count);
        Assert.DoesNotContain("session=secret-cookie", server.RecordedRequests[1].GetHeader("Cookie") ?? string.Empty);
        Assert.False(context.Data.COOKIES.ContainsKey("session"));
    }

    [Fact]
    public async Task HttpLogs_RedactSensitiveHeadersCookiesAndPayloads()
    {
        const string authSecret = "Bearer secret-token";
        const string cookieSecret = "session=secret-cookie";
        var largePayload = new string('x', 20000);
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(TestHttpResponse.WithCookies(new[] { "server=secret-set-cookie; Path=/" }, body: largePayload)),
            expectedRequests: 1);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(
            context.Data,
            HttpLibrary.SystemNet,
            server.Uri,
            customHeaders: new Dictionary<string, string>
            {
                ["Authorization"] = authSecret,
                ["Cookie"] = cookieSecret
            });

        var log = string.Join("\n", context.Data.Logger.Entries.Select(e => e.Message));
        Assert.DoesNotContain(authSecret, log);
        Assert.DoesNotContain(cookieSecret, log);
        Assert.DoesNotContain("secret-set-cookie", log);
        Assert.Contains("[redacted]", log);
        Assert.Contains("[TRUNCATED", log);
    }
}
