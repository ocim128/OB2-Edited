using RuriLib.Functions.Http;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace RuriLib.Tests.Functions.Http;

/// <summary>
/// <see cref="NetworkExceptionHelper"/> decides whether a failed bot retries with exponential
/// backoff or fails immediately, and how long it waits. Both halves are observable to users:
/// a misclassified exception burns retries, and a miscomputed delay either hot-loops or stalls.
/// </summary>
public class NetworkExceptionHelperTests
{
    [Theory]
    [MemberData(nameof(DirectNetworkExceptions))]
    public void IsNetworkException_ReturnsTrueForNetworkExceptionTypes(Exception exception)
        => Assert.True(NetworkExceptionHelper.IsNetworkException(exception));

    public static IEnumerable<object[]> DirectNetworkExceptions()
    {
        yield return [new HttpRequestException("boom")];
        yield return [new SocketException((int)SocketError.HostNotFound)];
        yield return [new TimeoutException("boom")];
        // WebException is status-dependent; the retryable statuses are covered separately below.
        yield return [new WebException("boom", WebExceptionStatus.ConnectFailure)];
    }

    [Fact]
    public void IsNetworkException_ReturnsTrueForOperationCanceled()
        => Assert.True(NetworkExceptionHelper.IsNetworkException(new OperationCanceledException()));

    [Fact]
    public void IsNetworkException_ReturnsTrueForIoExceptionWrappingSocketException()
        => Assert.True(NetworkExceptionHelper.IsNetworkException(
            new IOException("read failed", new SocketException((int)SocketError.ConnectionReset))));

    [Theory]
    [InlineData(WebExceptionStatus.ConnectFailure)]
    [InlineData(WebExceptionStatus.NameResolutionFailure)]
    [InlineData(WebExceptionStatus.ProxyNameResolutionFailure)]
    [InlineData(WebExceptionStatus.Timeout)]
    [InlineData(WebExceptionStatus.RequestCanceled)]
    public void IsNetworkException_ReturnsTrueForNetworkWebExceptionStatuses(WebExceptionStatus status)
        => Assert.True(NetworkExceptionHelper.IsNetworkException(new WebException("boom", status)));

    [Theory]
    [InlineData(WebExceptionStatus.ProtocolError)]
    [InlineData(WebExceptionStatus.Success)]
    public void IsNetworkException_ReturnsFalseForNonNetworkWebExceptionStatuses(WebExceptionStatus status)
    {
        // Regression test for a real defect: WebException used to sit in the NetworkExceptionTypes
        // fast-path set, whose check returned true before the NetworkWebStatuses filter could run.
        // That made the status filter unreachable, so every WebException - including ProtocolError
        // (HTTP 4xx/5xx) - was classified as retryable. The type was removed from the fast-path set
        // so WebException is now judged by status, which is the intended contract.
        Assert.False(NetworkExceptionHelper.IsNetworkException(new WebException("boom", status)));
    }

    [Fact]
    public void IsNetworkException_ReturnsTrueForProtocolErrorWrappingRetryableInnerException()
    {
        // The status filter must reject the ProtocolError itself but still let the inner-exception
        // walk find a genuinely retryable cause buried underneath it.
        var exception = new WebException(
            "boom",
            new SocketException((int)SocketError.ConnectionReset),
            WebExceptionStatus.ProtocolError,
            null);

        Assert.True(NetworkExceptionHelper.IsNetworkException(exception));
    }

    [Fact]
    public void IsNetworkException_ReturnsFalseForUnrelatedExceptions()
        => Assert.False(NetworkExceptionHelper.IsNetworkException(new InvalidOperationException("boom")));

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(NotSupportedException))]
    public void IsNetworkException_DoesNotClassifyApplicationErrorsAsRetryable(Type exceptionType)
    {
        // Guards the other direction of the retry decision: a fix for the WebException defect
        // above must not widen the fast-path set to ordinary application errors.
        var exception = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        Assert.False(NetworkExceptionHelper.IsNetworkException(exception));
    }

    [Fact]
    public void IsNetworkException_ReturnsFalseForNull()
        => Assert.False(NetworkExceptionHelper.IsNetworkException(null));

    [Fact]
    public void IsNetworkException_DetectsNetworkExceptionNestedInInnerExceptionChain()
        => Assert.True(NetworkExceptionHelper.IsNetworkException(
            new InvalidOperationException("outer",
                new AggregateException(new SocketException((int)SocketError.TimedOut)))));

    [Fact]
    public void IsNetworkException_StopsWalkingInnerExceptionsAtDepthLimit()
    {
        // The helper caps the inner-exception walk at depth 5 to bound the scan. A network
        // exception buried deeper than that is intentionally not classified as retryable.
        Exception exception = new SocketException((int)SocketError.TimedOut);

        for (var i = 0; i < 7; i++)
        {
            exception = new Exception($"layer {i}", exception);
        }

        Assert.False(NetworkExceptionHelper.IsNetworkException(exception));
    }

    [Fact]
    public void CalculateBackoffDelay_ReturnsBaseDelayForFirstAttempt()
        => Assert.Equal(100, NetworkExceptionHelper.CalculateBackoffDelay(0));

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-5, 100)]
    public void CalculateBackoffDelay_ClampsNonPositiveRetryCountToBaseDelay(int retryCount, int expected)
        => Assert.Equal(expected, NetworkExceptionHelper.CalculateBackoffDelay(retryCount));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public void CalculateBackoffDelay_GrowsExponentiallyWithinJitterBand(int retryCount)
    {
        var baseDelayMs = 100;
        var delay = NetworkExceptionHelper.CalculateBackoffDelay(retryCount, baseDelayMs);
        var exponentialFloor = baseDelayMs * (int)Math.Pow(2, retryCount);

        // delay = baseDelay * 2^retryCount + jitter, where jitter is in [0, baseDelay).
        Assert.InRange(delay, exponentialFloor, exponentialFloor + baseDelayMs - 1);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void CalculateBackoffDelay_IsCappedAtMaxDelay(int retryCount)
    {
        var maxDelayMs = 30_000;

        var delay = NetworkExceptionHelper.CalculateBackoffDelay(retryCount, 100, maxDelayMs);

        Assert.InRange(delay, 0, maxDelayMs);
    }

    [Fact]
    public void CalculateBackoffDelay_DoesNotOverflowForHugeRetryCounts()
    {
        // The power-of-two table is capped at index 16, so large retry counts must still
        // return a sane value instead of overflowing or throwing.
        var delay = NetworkExceptionHelper.CalculateBackoffDelay(int.MaxValue, 100, 30_000);

        Assert.InRange(delay, 0, 30_000);
    }

    [Fact]
    public void CalculateBackoffDelay_RespectsCustomMaxDelay()
    {
        var delay = NetworkExceptionHelper.CalculateBackoffDelay(10, 100, 500);

        Assert.InRange(delay, 0, 500);
    }
}
