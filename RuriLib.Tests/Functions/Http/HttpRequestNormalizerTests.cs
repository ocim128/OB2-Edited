using RuriLib.Functions.Http;

namespace RuriLib.Tests.Functions.Http;

/// <summary>
/// Covers the two pure helpers of <c>HttpRequestNormalizer</c> that shape every outgoing request:
/// header case-normalization (so a config cannot smuggle a duplicate of a framework header) and
/// cookie-jar merging (so a <c>Cookie:</c> header lands in the bot's cookie jar / <c>data.COOKIES</c>).
/// </summary>
public class HttpRequestNormalizerTests
{
    [Fact]
    public void NormalizeSingleValueHeaders_IsCaseInsensitiveSoLaterValueWins()
    {
        var normalized = HttpRequestNormalizer.NormalizeSingleValueHeaders(new[]
        {
            new KeyValuePair<string, string>("User-Agent", "first"),
            new KeyValuePair<string, string>("user-agent", "second")
        });

        Assert.Single(normalized);
        Assert.Equal("second", normalized["USER-AGENT"]);
    }

    [Fact]
    public void NormalizeSingleValueHeaders_PreservesDistinctHeaderNames()
    {
        var normalized = HttpRequestNormalizer.NormalizeSingleValueHeaders(new[]
        {
            new KeyValuePair<string, string>("Accept", "text/html"),
            new KeyValuePair<string, string>("User-Agent", "Flux")
        });

        Assert.Equal(2, normalized.Count);
        Assert.Equal("text/html", normalized["accept"]);
        Assert.Equal("Flux", normalized["User-Agent"]);
    }

    [Fact]
    public void NormalizeSingleValueHeaders_HandlesNullAndEmpty()
    {
        Assert.Empty(HttpRequestNormalizer.NormalizeSingleValueHeaders(null));
        Assert.Empty(HttpRequestNormalizer.NormalizeSingleValueHeaders(Array.Empty<KeyValuePair<string, string>>()));
    }

    [Fact]
    public void ParseCipherSuites_ReturnsEmptyForNull()
        => Assert.Empty(HttpRequestNormalizer.ParseCipherSuites(null));

    [Fact]
    public void ParseCipherSuites_ParsesNamedSuites()
    {
        var suites = HttpRequestNormalizer.ParseCipherSuites(
            ["TLS_AES_128_GCM_SHA256", "TLS_AES_256_GCM_SHA384"]);

        Assert.Equal(2, suites.Length);
        Assert.Equal(System.Net.Security.TlsCipherSuite.TLS_AES_128_GCM_SHA256, suites[0]);
        Assert.Equal(System.Net.Security.TlsCipherSuite.TLS_AES_256_GCM_SHA384, suites[1]);
    }

    [Fact]
    public void ParseCipherSuites_ThrowsNotSupportedForUnknownSuite()
    {
        var exception = Assert.Throws<NotSupportedException>(
            () => HttpRequestNormalizer.ParseCipherSuites(["NOT_A_REAL_SUITE"]));

        Assert.Contains("NOT_A_REAL_SUITE", exception.Message);
    }
}
