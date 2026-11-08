using RuriLib.Blocks.Requests.Http;
using RuriLib.Functions.Http.Options;
using RuriLib.Tests.Infrastructure;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Tests.Functions.Http;

public class HttpRequestTimeoutTests
{
    [Theory]
    [InlineData(HttpLibrary.RuriLibHttp, 4000)]
    [InlineData(HttpLibrary.RuriLibHttp, -1)]
    [InlineData(HttpLibrary.SystemNet, 4000)]
    [InlineData(HttpLibrary.SystemNet, -1)]
    public async Task SlowPostResponse_UsesBlockTimeoutInsteadOfProxyReadWriteTimeout(
        HttpLibrary library, int timeoutMilliseconds)
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(new TestHttpResponse
            {
                Body = "delayed response",
                ResponseDelay = TimeSpan.FromMilliseconds(500)
            }));
        using var context = new HttpTransportTestContext();
        context.Settings.RuriLibSettings.ProxySettings.ProxyReadWriteTimeoutMilliseconds = 100;

        await Methods.HttpRequestStandard(context.Data, CreateOptions(library, server.Uri, timeoutMilliseconds))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(200, context.Data.RESPONSECODE);
        Assert.Equal("delayed response", context.Data.SOURCE);
        // A slow POST must be allowed to complete without replaying its side effects.
        Assert.Single(server.RecordedRequests);
    }

    [Theory]
    [InlineData(HttpLibrary.RuriLibHttp)]
    [InlineData(HttpLibrary.SystemNet)]
    public async Task BlockTimeout_ReportsTimeoutWithConfiguredLimit(HttpLibrary library)
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(new TestHttpResponse
            {
                ResponseDelay = TimeSpan.FromSeconds(5)
            }));
        using var context = new HttpTransportTestContext();
        context.Settings.RuriLibSettings.ProxySettings.ProxyReadWriteTimeoutMilliseconds = 10000;

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            Methods.HttpRequestStandard(context.Data, CreateOptions(library, server.Uri, 500))
                .WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Contains("500ms", exception.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
        Assert.Contains(context.Data.Logger.Entries, entry =>
            entry.Message.Contains("HTTP exception") && entry.Message.Contains("TimeoutException"));
        Assert.Single(server.RecordedRequests);
    }

    [Theory]
    [InlineData(HttpLibrary.RuriLibHttp)]
    [InlineData(HttpLibrary.SystemNet)]
    public async Task JobCancellation_RemainsCancellationWhenBlockTimeoutIsDisabled(HttpLibrary library)
    {
        using var cancellation = new CancellationTokenSource();
        await using var server = await TestHttpServer.StartAsync(HttpTestScenario.WithHandler((_, _) =>
        {
            cancellation.Cancel();
            return new TestHttpResponse { ResponseDelay = TimeSpan.FromSeconds(5) };
        }));
        using var context = new HttpTransportTestContext();
        context.Data.CancellationToken = cancellation.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Methods.HttpRequestStandard(context.Data, CreateOptions(library, server.Uri, -1))
                .WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.DoesNotContain(context.Data.Logger.Entries, entry => entry.Message.Contains("TimeoutException"));
        Assert.Single(server.RecordedRequests);
    }

    private static StandardHttpRequestOptions CreateOptions(HttpLibrary library, Uri uri, int timeoutMilliseconds)
        => new()
        {
            Url = uri.ToString(),
            Method = RuriLib.Functions.Http.HttpMethod.POST,
            HttpLibrary = library,
            HttpVersion = "1.1",
            Content = "query=test",
            ContentType = "application/x-www-form-urlencoded",
            AutoRedirect = false,
            TimeoutMilliseconds = timeoutMilliseconds
        };
}
