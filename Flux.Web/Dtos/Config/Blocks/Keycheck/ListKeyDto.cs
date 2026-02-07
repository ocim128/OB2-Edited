using Flux.Web.Attributes;
using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Conditions.Comparisons;

namespace Flux.Web.Dtos.Config.Blocks.Keycheck;

/// <summary>
/// A list key of the keychain.
/// </summary>
[PolyType("listKey")]
[MapsFrom(typeof(ListKey))]
public class ListKeyDto : KeyDto
{
    /// <summary>
    /// The comparison condition.
    /// </summary>
    public ListComparison Comparison { get; set; }
}
