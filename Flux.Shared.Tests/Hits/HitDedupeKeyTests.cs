using Flux.Core.Entities;
using Flux.Core.Models.Hits;

namespace Flux.Shared.Tests.Hits;

public class HitDedupeKeyTests
{
    [Fact]
    public void From_IgnoresWordlistName_WhenConfigured()
    {
        var first = new HitEntity { Data = "data", ConfigName = "config", WordlistName = "one" };
        var second = new HitEntity { Data = "data", ConfigName = "config", WordlistName = "two" };

        Assert.Equal(HitDedupeKey.From(first, ignoreWordlistName: true), HitDedupeKey.From(second, ignoreWordlistName: true));
    }

    [Fact]
    public void From_IncludesWordlistName_WhenConfigured()
    {
        var first = new HitEntity { Data = "data", ConfigName = "config", WordlistName = "one" };
        var second = new HitEntity { Data = "data", ConfigName = "config", WordlistName = "two" };

        Assert.NotEqual(HitDedupeKey.From(first, ignoreWordlistName: false), HitDedupeKey.From(second, ignoreWordlistName: false));
    }
}
