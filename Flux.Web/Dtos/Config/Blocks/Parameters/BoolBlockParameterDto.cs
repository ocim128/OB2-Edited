using Flux.Web.Attributes;
using RuriLib.Models.Blocks.Parameters;

namespace Flux.Web.Dtos.Config.Blocks.Parameters;

/// <summary>
/// A boolean block parameter.
/// </summary>
[PolyType("boolParam")]
[MapsFrom(typeof(BoolParameter))]
public class BoolBlockParameterDto : BlockParameterDto
{
    /// <summary>
    /// The default value.
    /// </summary>
    public bool DefaultValue { get; set; } = false;
}
