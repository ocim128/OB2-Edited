using System.Collections.Generic;
using OpenBullet2.Web.Dtos.Config.Blocks.Keycheck;
using RuriLib.Models.Blocks.Custom.Keycheck;

namespace OpenBullet2.Web.Dtos.Config.Blocks;

/// <summary>
/// Represents a conditional override entry for the Constant String block.
/// </summary>
public class ConditionalConstantCaseDto
{
    public string Name { get; set; } = string.Empty;
    public KeychainMode Mode { get; set; } = KeychainMode.OR;
    public BlockSettingDto Value { get; set; } = new();
    public List<object> Keys { get; set; } = new();
}
