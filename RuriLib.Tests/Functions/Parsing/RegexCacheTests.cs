using System.Text.RegularExpressions;
using RuriLib.Functions.Parsing;
using RuriLib.Models.Data.Rules;

namespace RuriLib.Tests.Functions.Parsing;

public class RegexCacheTests
{
    [Fact]
    public void GetOrCreate_UsesConfiguredTimeout()
    {
        RegexCache.Clear();
        var regex = RegexCache.GetOrCreate(
            "^(a+)+$",
            compile: false,
            matchTimeout: TimeSpan.FromMilliseconds(1));

        Assert.Throws<RegexMatchTimeoutException>(() => regex.IsMatch(new string('a', 10_000) + "!"));
    }

    [Fact]
    public void GetOrCreate_BoundsCacheGrowth()
    {
        RegexCache.Clear();

        for (var i = 0; i < 600; i++)
        {
            _ = RegexCache.GetOrCreate($"^{i}$");
        }

        Assert.InRange(RegexCache.CacheCount, 1, 512);
    }

    [Fact]
    public void RegexDataRule_EmptyPatternKeepsRegexIsMatchSemantics()
    {
        var rule = new RegexDataRule { RegexToMatch = string.Empty };

        Assert.True(rule.IsSatisfied("anything"));

        rule.Invert = true;

        Assert.False(rule.IsSatisfied("anything"));
    }
}
