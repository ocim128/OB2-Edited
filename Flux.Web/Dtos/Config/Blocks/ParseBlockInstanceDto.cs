using System.Collections.Generic;
using RuriLib.Models.Blocks.Custom.Parse;

namespace Flux.Web.Dtos.Config.Blocks;

/// <summary>
/// DTO that represents a parse block instance.
/// </summary>
public class ParseBlockInstanceDto : BlockInstanceDto
{
    /// <summary>
    /// The name of the output variable that will be created to
    /// hold the result of the block's computation.
    /// </summary>
    public string OutputVariable { get; set; } = string.Empty;

    /// <summary>
    /// Whether to parse recursively.
    /// </summary>
    public bool Recursive { get; set; }

    /// <summary>
    /// Whether the variable created should be marked as capture and saved.
    /// </summary>
    public bool IsCapture { get; set; }

    /// <summary>
    /// Whether any error in the block should be safely caught, without
    /// interrupting the execution.
    /// </summary>
    public bool Safe { get; set; }

    /// <summary>
    /// The parsing mode.
    /// </summary>
    public ParseMode Mode { get; set; }

    /// <summary>
    /// Optional conditional overrides for the parsed value.
    /// </summary>
    public List<ConditionalConstantCaseDto> ConditionalCases { get; set; } = new();
}
