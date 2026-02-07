using Flux.Web.Attributes;
using RuriLib.Models.Blocks.Parameters;

namespace Flux.Web.Dtos.Config.Blocks.Parameters;

/// <summary>
/// A byte array block parameter.
/// </summary>
[PolyType("byteArrayParam")]
[MapsFrom(typeof(ByteArrayParameter))]
public class ByteArrayBlockParameterDto : BlockParameterDto
{
    /// <summary>
    /// The default value.
    /// </summary>
    public byte[] DefaultValue { get; set; } = Array.Empty<byte>();
}
