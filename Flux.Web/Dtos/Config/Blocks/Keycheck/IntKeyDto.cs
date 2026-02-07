using Flux.Web.Attributes;
using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Conditions.Comparisons;

namespace Flux.Web.Dtos.Config.Blocks.Keycheck;

/// <summary>
/// An integer key of the keychain.
/// </summary>
[PolyType("intKey")]
[MapsFrom(typeof(IntKey))]
public class IntKeyDto : KeyDto
{
    /// <summary>
    /// The comparison condition.
    /// </summary>
    public NumComparison Comparison { get; set; }
}
