using Flux.Web.Attributes;
using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Conditions.Comparisons;

namespace Flux.Web.Dtos.Config.Blocks.Keycheck;

/// <summary>
/// A floating point key of the keychain.
/// </summary>
[PolyType("floatKey")]
[MapsFrom(typeof(FloatKey))]
public class FloatKeyDto : KeyDto
{
    /// <summary>
    /// The comparison condition.
    /// </summary>
    public NumComparison Comparison { get; set; }
}
