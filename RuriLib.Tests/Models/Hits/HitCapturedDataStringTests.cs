using System.Collections.Generic;
using RuriLib.Models.Hits;
using Xunit;

namespace RuriLib.Tests.Models.Hits;

public class HitCapturedDataStringTests
{
    [Fact]
    public void CapturedDataString_ReflectsCapturedVariables()
    {
        var hit = new Hit
        {
            CapturedData = new Dictionary<string, object>
            {
                ["USER"] = "alice",
                ["PASS"] = "secret"
            }
        };

        Assert.Equal("USER = alice | PASS = secret", hit.CapturedDataString);
    }

    [Fact]
    public void CapturedDataString_IsCachedAcrossReads()
    {
        var hit = new Hit
        {
            CapturedData = new Dictionary<string, object> { ["USER"] = "alice" }
        };

        // The job viewer reads this once per hit per poll; the returned instance must be reused
        // so the projection does not re-run VariableFactory for every hit on every tick.
        Assert.Same(hit.CapturedDataString, hit.CapturedDataString);
    }

    [Fact]
    public void CapturedDataString_IsInvalidatedWhenCapturedDataChanges()
    {
        var hit = new Hit
        {
            CapturedData = new Dictionary<string, object> { ["USER"] = "alice" }
        };

        Assert.Equal("USER = alice", hit.CapturedDataString);

        hit.CapturedData = new Dictionary<string, object> { ["USER"] = "bob" };

        // A stale cache here would display the wrong captures for the hit.
        Assert.Equal("USER = bob", hit.CapturedDataString);
    }
}
