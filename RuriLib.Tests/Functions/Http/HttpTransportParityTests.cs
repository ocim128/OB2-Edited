using RuriLib.Functions.Http.Options;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace RuriLib.Tests.Functions.Http;

using RequestMethod = RuriLib.Functions.Http.HttpMethod;

public class HttpTransportParityTests
{
    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task BasicTextResponse_ReturnsExpectedStatusCodeAndBody(HttpLibrary library)
    {
        await using var server = await TestHttpServer.StartAsync("OK", expectedRequests: 1);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(context.Data, library, server.Uri);

        Assert.Equal(200, context.Data.RESPONSECODE);
        Assert.Equal("OK", context.Data.SOURCE);
        Assert.True(HttpTransportTestHelper.ByteArraysEqual(Encoding.UTF8.GetBytes("OK"), context.Data.RAWSOURCE));
    }

    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task ChunkedResponse_ReadsCompleteBody(HttpLibrary library)
    {
        const string body = "Wikipedia in chunks";
        var response = new TestHttpResponse
        {
            Headers = new Dictionary<string, string> { ["Content-Type"] = "text/plain; charset=utf-8" },
            Body = body,
            UseChunkedTransferEncoding = true
        };

        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(response),
            expectedRequests: 1);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(context.Data, library, server.Uri);

        Assert.Equal(200, context.Data.RESPONSECODE);
        Assert.Equal(body, context.Data.SOURCE);
        Assert.True(HttpTransportTestHelper.ByteArraysEqual(Encoding.UTF8.GetBytes(body), context.Data.RAWSOURCE));
    }

    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task DuplicateHeaders_DifferentCasing_AreMergedOrAccessible(HttpLibrary library)
    {
        var response = new TestHttpResponse
        {
            Headers = new Dictionary<string, string> { ["X-Test"] = "alpha" },
            AdditionalHeaders = { ("x-test", "beta") }
        };

        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(response),
            expectedRequests: 1);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(context.Data, library, server.Uri);

        var mergedValue = HttpTransportTestHelper.GetMergedHeaderValue(context.Data.HEADERS, "X-Test");
        Assert.Contains("alpha", mergedValue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beta", mergedValue, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task RedirectResponseBody_Preserved_WhenAutoRedirectDisabled(HttpLibrary library)
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(TestHttpResponse.Redirect("/next", "redirect-body")),
            expectedRequests: 1);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(
            context.Data,
            library,
            server.Uri,
            autoRedirect: false,
            maxRedirects: 0);

        Assert.Equal(302, context.Data.RESPONSECODE);
        Assert.Equal("redirect-body", context.Data.SOURCE);
    }

    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task Redirect307_PreservesMethodAndBody(HttpLibrary library)
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(
                TestHttpResponse.Redirect("/final", statusCode: HttpStatusCode.TemporaryRedirect),
                TestHttpResponse.Ok("done")),
            expectedRequests: 2);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(
            context.Data,
            library,
            server.Uri,
            method: RequestMethod.POST,
            content: "payload-307",
            alwaysSendContent: true);

        Assert.Equal(2, server.RecordedRequests.Count);
        Assert.Equal("POST", server.RecordedRequests[1].Method);
        Assert.Equal("/final", server.RecordedRequests[1].Path);
        Assert.Equal("payload-307", server.RecordedRequests[1].Body);
    }

    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task Redirect308_PreservesMethodAndBody(HttpLibrary library)
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(
                TestHttpResponse.Redirect("/final", statusCode: HttpStatusCode.PermanentRedirect),
                TestHttpResponse.Ok("done")),
            expectedRequests: 2);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(
            context.Data,
            library,
            server.Uri,
            method: RequestMethod.POST,
            content: "payload-308",
            alwaysSendContent: true);

        Assert.Equal(2, server.RecordedRequests.Count);
        Assert.Equal("POST", server.RecordedRequests[1].Method);
        Assert.Equal("/final", server.RecordedRequests[1].Path);
        Assert.Equal("payload-308", server.RecordedRequests[1].Body);
    }

    [Theory(Skip = "HTTPS test server not implemented yet")]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public Task HttpsToHttpRedirect_BlockedByDefault(HttpLibrary library)
    {
        _ = library;
        return Task.CompletedTask;
    }

    [Theory(Skip = "HTTPS test server not implemented yet")]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public Task HttpsToHttpRedirect_Allowed_WhenInsecureRedirectEnabled(HttpLibrary library)
    {
        _ = library;
        return Task.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task Cookies_SetOnResponse_AvailableOnSubsequentRequest(HttpLibrary library)
    {
        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(
                TestHttpResponse.WithCookies(new[] { "session=abc123; Path=/" }),
                TestHttpResponse.Ok("second")),
            expectedRequests: 2);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(context.Data, library, server.Uri, autoRedirect: false, maxRedirects: 0);
        await HttpTransportTestHelper.SendStandardAsync(context.Data, library, server.Uri, autoRedirect: false, maxRedirects: 0);

        Assert.Equal(2, server.RecordedRequests.Count);
        Assert.Contains("session=abc123", server.RecordedRequests[1].GetHeader("Cookie"));
        Assert.Equal("abc123", context.Data.COOKIES["session"]);
    }

    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task BinaryResponse_RoundTripsWithoutCorruption(HttpLibrary library)
    {
        var payload = new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFF, 0x42, 0x10 };
        var response = new TestHttpResponse
        {
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/octet-stream" },
            RawBody = payload
        };

        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(response),
            expectedRequests: 1);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(context.Data, library, server.Uri);

        Assert.True(HttpTransportTestHelper.ByteArraysEqual(payload, context.Data.RAWSOURCE));
    }

    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task LargeContentLengthResponse_RoundTripsWithoutTrailingBytes(HttpLibrary library)
    {
        var payload = new byte[6000];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 251);
        }

        var response = new TestHttpResponse
        {
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/octet-stream" },
            RawBody = payload
        };

        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(response),
            expectedRequests: 1);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(context.Data, library, server.Uri);

        Assert.Equal(payload.Length, context.Data.RAWSOURCE.Length);
        Assert.True(HttpTransportTestHelper.ByteArraysEqual(payload, context.Data.RAWSOURCE));
    }

    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task NonUtf8Response_DecodedCorrectly_WithCodePagesEncoding(HttpLibrary library)
    {
        const string text = "caf\xe9 \xa3";
        var payload = HttpTransportTestHelper.GetWindows1252Bytes(text);
        var response = new TestHttpResponse
        {
            Headers = new Dictionary<string, string> { ["Content-Type"] = "text/plain; charset=windows-1252" },
            RawBody = payload
        };

        await using var server = await TestHttpServer.StartAsync(
            HttpTestScenario.FromResponses(response),
            expectedRequests: 1);
        using var context = new HttpTransportTestContext();

        await HttpTransportTestHelper.SendStandardAsync(
            context.Data,
            library,
            server.Uri,
            codePagesEncoding: "windows-1252");

        Assert.Equal(text, context.Data.SOURCE);
        Assert.True(HttpTransportTestHelper.ByteArraysEqual(payload, context.Data.RAWSOURCE));
    }

    [Theory]
    [MemberData(nameof(HttpTransportTestHelper.AllLibraries), MemberType = typeof(HttpTransportTestHelper))]
    public async Task MultipartUpload_BinaryFileAndRawBytes_ReceivedCorrectly(HttpLibrary library)
    {
        await using var server = await TestHttpServer.StartAsync("OK", expectedRequests: 1);
        using var context = new HttpTransportTestContext();

        var fileBytes = new byte[] { 0xFA, 0xFB, 0x00, 0x01, 0x02, 0xFC };
        var rawBytes = new byte[] { 0x10, 0x11, 0x12, 0x80, 0xFF };
        var filePath = System.IO.Path.Combine(context.Workspace.RootPath, "payload.bin");
        await System.IO.File.WriteAllBytesAsync(filePath, fileBytes);

        var contents = new MyHttpContent[]
        {
            new StringHttpContent("field", "value", "text/plain"),
            new RawHttpContent("raw", rawBytes, "application/octet-stream"),
            new FileHttpContent("file", filePath, "application/octet-stream")
        };

        await HttpTransportTestHelper.SendMultipartAsync(context.Data, library, server.Uri, contents);

        var request = Assert.Single(server.RecordedRequests);
        Assert.True(HttpTransportTestHelper.ContainsSubsequence(request.RawBody, fileBytes));
        Assert.True(HttpTransportTestHelper.ContainsSubsequence(request.RawBody, rawBytes));
        Assert.True(HttpTransportTestHelper.ContainsSubsequence(request.RawBody, HttpTransportTestHelper.GetAsciiBytes("payload.bin")));
    }
}
