using RuriLib.Functions.Http.Options;
using RuriLib.Tests.Infrastructure;
using System.Net;
using System.Threading.Tasks;

namespace RuriLib.Tests.Functions.Http;

using RequestMethod = RuriLib.Functions.Http.HttpMethod;

public class HttpRedirectSemanticsTests
{
    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task Redirect301_PreservesDeleteMethodAndBody(HttpLibrary library)
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(
                TestHttpResponse.Redirect("/final", statusCode: HttpStatusCode.MovedPermanently),
                TestHttpResponse.Ok("done")),
            expectedRequests: 2);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(
            context.Data,
            library,
            server.Uri,
            method: RequestMethod.DELETE,
            content: "delete-body",
            alwaysSendContent: true);

        Assert.Equal("DELETE", server.RecordedRequests[1].Method);
        Assert.Equal("delete-body", server.RecordedRequests[1].Body);
    }

    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task Redirect302_PreservesPutMethodAndBody(HttpLibrary library)
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(
                TestHttpResponse.Redirect("/final", statusCode: HttpStatusCode.Found),
                TestHttpResponse.Ok("done")),
            expectedRequests: 2);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(
            context.Data,
            library,
            server.Uri,
            method: RequestMethod.PUT,
            content: "put-body",
            alwaysSendContent: true);

        Assert.Equal("PUT", server.RecordedRequests[1].Method);
        Assert.Equal("put-body", server.RecordedRequests[1].Body);
    }

    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task Redirect303_PreservesHeadMethod(HttpLibrary library)
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(
                TestHttpResponse.Redirect("/final", statusCode: HttpStatusCode.SeeOther),
                TestHttpResponse.Ok("done")),
            expectedRequests: 2);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(
            context.Data,
            library,
            server.Uri,
            method: RequestMethod.HEAD);

        Assert.Equal("HEAD", server.RecordedRequests[1].Method);
        Assert.Empty(server.RecordedRequests[1].RawBody);
    }
}
