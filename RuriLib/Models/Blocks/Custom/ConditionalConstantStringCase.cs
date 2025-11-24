using System.Collections.Generic;
using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Blocks.Settings;

namespace RuriLib.Models.Blocks.Custom;

/// <summary>
/// Represents a conditional override for the Constant String block.
/// </summary>
public class ConditionalConstantStringCase
{
    private const string ValueSettingName = "conditionalValue";

    public ConditionalConstantStringCase()
    {
        Value = BlockSettingFactory.CreateStringSetting(ValueSettingName, string.Empty, SettingInputMode.Fixed, multiLine: true, readableName: "Override Value");
    }

    /// <summary>
    /// Human friendly name used in the UI.
    /// </summary>
    public string Name { get; set; } = "Condition";

    /// <summary>
    /// Logical operator used to combine the keys in this case.
    /// </summary>
    public KeychainMode Mode { get; set; } = KeychainMode.OR;

    /// <summary>
    /// The keys that must match for this case to trigger.
    /// </summary>
    public List<Key> Keys { get; set; } = new();

    /// <summary>
    /// The value that should override the constant when the case matches.
    /// </summary>
    public BlockSetting Value { get; }
}
