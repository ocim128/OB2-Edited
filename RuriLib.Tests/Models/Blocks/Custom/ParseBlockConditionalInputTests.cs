using RuriLib.Models.Blocks.Custom;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Configs;

namespace RuriLib.Tests.Models.Blocks.Custom;

public class ParseBlockConditionalInputTests
{
    [Fact]
    public void CreateConditionalCase_ClonesMainInputSetting()
    {
        var block = CreateParseBlock();
        block.Settings["input"].InputVariableName = "mainInput";

        var conditionalCase = block.CreateConditionalCase();

        Assert.True(conditionalCase.Settings.ContainsKey("input"));
        Assert.Equal(SettingInputMode.Variable, conditionalCase.Settings["input"].InputMode);
        Assert.Equal("mainInput", conditionalCase.Settings["input"].InputVariableName);
    }

    [Fact]
    public void ToCSharp_UsesConditionalInputOverride()
    {
        var block = CreateParseBlock();
        var conditionalCase = block.CreateConditionalCase();
        conditionalCase.Settings["input"].InputVariableName = "caseInput";
        block.ConditionalCases.Add(conditionalCase);

        var csharp = block.ToCSharp(["caseInput"], new ConfigSettings());

        Assert.Contains("ParseBetweenStrings(data, (string)((object)caseInput).DynamicAsString()", csharp);
    }

    [Fact]
    public void FromLc_ParsesConditionalInputOverride()
    {
        var script = """
              input = @mainInput
              MODE:LR
              => VAR @parseOutput
              CASE "Condition 1" OR
                CASEMODE LR
                input = @caseInput
              ENDCASE
            """;
        var lineNumber = 0;
        var parsed = CreateParseBlock();
        parsed.FromLC(ref script, ref lineNumber);

        Assert.Single(parsed.ConditionalCases);
        Assert.Equal("caseInput", parsed.ConditionalCases[0].Settings["input"].InputVariableName);
    }

    private static ParseBlockInstance CreateParseBlock()
        => new(new ParseBlockDescriptor());
}
