using System;
using System.Collections.Generic;

namespace RuriLib.Tests.Infrastructure;

/// <summary>
/// Describes a test scenario consisting of a sequence of <see cref="TestHttpResponse"/>s
/// and/or per-request handler functions. Instances are passed to
/// <see cref="TestHttpServer.StartAsync(RuriLib.Tests.Infrastructure.HttpTestScenario)"/>.
/// </summary>
public sealed class HttpTestScenario
{
    /// <summary>
    /// A fixed list of responses to serve in order. When this is non-empty,
    /// the handler at <see cref="RequestHandler"/> is invoked first (if set) and
    /// may override the response; otherwise the responses are taken from this list.
    /// If there are fewer entries than incoming requests, the last entry is reused.
    /// </summary>
    public List<TestHttpResponse> Responses { get; init; } = new();

    /// <summary>
    /// An optional per-request handler. It receives the incoming
    /// <see cref="RecordedHttpRequest"/> and the zero-based request index and
    /// returns the <see cref="TestHttpResponse"/> to send.
    /// When <c>null</c>, responses are taken from <see cref="Responses"/>.
    /// When both are set, this handler takes precedence (if it returns non-<c>null</c>);
    /// a <c>null</c> return falls back to <see cref="Responses"/>.
    /// </summary>
    public Func<RecordedHttpRequest, int, TestHttpResponse?>? RequestHandler { get; init; }

    // -----------------------------------------------------------------
    // Convenience factories
    // -----------------------------------------------------------------

    /// <summary>
    /// Creates a scenario that always returns 200 OK with the given body.
    /// </summary>
    public static HttpTestScenario AlwaysOk(string body = "OK", int expectedRequests = 1) => new()
    {
        Responses = { TestHttpResponse.Ok(body) },
    };

    /// <summary>
    /// Creates a scenario that serves the given responses in order.
    /// </summary>
    public static HttpTestScenario FromResponses(params TestHttpResponse[] responses) => new()
    {
        Responses = new List<TestHttpResponse>(responses)
    };

    /// <summary>
    /// Creates a scenario that delegates every request to the given handler.
    /// </summary>
    public static HttpTestScenario WithHandler(
        Func<RecordedHttpRequest, int, TestHttpResponse?> handler) => new()
    {
        RequestHandler = handler
    };
}
