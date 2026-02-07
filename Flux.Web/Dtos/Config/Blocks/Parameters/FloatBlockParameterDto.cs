using Flux.Web.Attributes;
using RuriLib.Models.Blocks.Parameters;

namespace Flux.Web.Dtos.Config.Blocks.Parameters;

/// <summary>
/// A floating point block parameter.
/// </summary>
[PolyType("floatParam")]
[MapsFrom(typeof(FloatParameter))]
public class FloatBlockParameterDto : BlockParameterDto
{
    /// <summary>
    /// The default value.
    /// </summary>
    public float DefaultValue { get; set; } = 0f;
}
