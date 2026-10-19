using RuriLib.Functions.Http;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace RuriLib.Tests.Functions.Http;

/// <summary>
/// <c>HttpExceptionClassifier</c> picks the log severity color for a failed request: network
/// failures log orange, everything else log red. It had the same WebException flaw as
/// <c>NetworkExceptionHelper</c> and is covered here so the two stay consistent.
/// </summary>
public class HttpExceptionClassifierTests
{
    [Theory]
    [MemberData(nameof(NetworkExceptions))]
    public void IsLikelyNetworkException_ReturnsTrueForNetworkFailures(Exception exception)
        => Assert.True(HttpExceptionClassifier.IsLikelyNetworkException(exception));

    public static IEnumerable<object[]> NetworkExceptions()
    {
        yield return [new HttpRequestException("boom")];
        yield return [new SocketException((int)SocketError.HostNotFound)];
        yield return [new TimeoutException("boom")];
        yield return [new OperationCanceledException()];
        yield return [new IOException("read failed", new SocketException((int)SocketError.ConnectionReset))];
        yield return [new WebException("boom", WebExceptionStatus.ConnectFailure)];
        yield return [new WebException("boom", WebExceptionStatus.NameResolutionFailure)];
    }

    [Theory]
    [InlineData(WebExceptionStatus.ProtocolError)]
    [InlineData(WebExceptionStatus.Success)]
    public void IsLikelyNetworkException_ReturnsFalseForNonNetworkWebExceptionStatuses(WebExceptionStatus status)
    {
        // Protocol errors (HTTP 4xx/5xx) are responses, not network faults, so they must not be
        // reported as network failures - neither for retry classification nor for log severity.
        Assert.False(HttpExceptionClassifier.IsLikelyNetworkException(new WebException("boom", status)));
    }

    [Fact]
    public void IsLikelyNetworkException_ReturnsFalseForApplicationErrors()
        => Assert.False(HttpExceptionClassifier.IsLikelyNetworkException(new InvalidOperationException("boom")));

    [Fact]
    public void IsLikelyNetworkException_ReturnsFalseForNull()
        => Assert.False(HttpExceptionClassifier.IsLikelyNetworkException(null));
}
