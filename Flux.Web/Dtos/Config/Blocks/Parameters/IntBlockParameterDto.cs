using Flux.Web.Attributes;
using RuriLib.Models.Blocks.Parameters;

namespace Flux.Web.Dtos.Config.Blocks.Parameters;

/// <summary>
/// An integer block parameter.
/// </summary>
[PolyType("intParam")]
[MapsFrom(typeof(IntParameter))]
public class IntBlockParameterDto : BlockParameterDto
{
    /// <summary>
    /// The default value.
    /// </summary>
    public int DefaultValue { get; set; } = 0;
}
