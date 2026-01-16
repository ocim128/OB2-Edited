using System.Collections.Generic;
using OpenBullet2.Web.Dtos.Config.Blocks.Keycheck;
using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Blocks.Custom.Parse;

namespace OpenBullet2.Web.Dtos.Config.Blocks;

/// <summary>
/// Represents a conditional override entry for the Constant String block.
/// </summary>
public class ConditionalConstantCaseDto
{
    /// <summary>The name of the entry.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The keychain mode.</summary>
    public KeychainMode Mode { get; set; } = KeychainMode.OR;

    /// <summary>The value to set.</summary>
    public BlockSettingDto Value { get; set; } = new();

    /// <summary>The keys to check.</summary>
    public List<object> Keys { get; set; } = new();

    /// <summary>The parse mode override.</summary>
    public ParseMode? ParseModeOverride { get; set; }

    /// <summary>The parse settings.</summary>
    public Dictionary<string, BlockSettingDto> ParseSettings { get; set; } = new();
}
