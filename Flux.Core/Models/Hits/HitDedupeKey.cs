using Flux.Core.Entities;

namespace Flux.Core.Models.Hits;

public readonly record struct HitDedupeKey(string Data, string ConfigName, string? WordlistName)
{
    public static HitDedupeKey From(HitEntity hit, bool ignoreWordlistName)
        => new(
            hit.Data,
            hit.ConfigName,
            ignoreWordlistName ? null : hit.WordlistName);
}
