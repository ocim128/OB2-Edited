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
        Assert.False(conditionalCase.InputOverridden);
    }

    [Fact]
    public void ToCSharp_InheritsMainInputUntilConditionalInputIsOverridden()
    {
        var block = CreateParseBlock();
        var conditionalCase = block.CreateConditionalCase();
        block.ConditionalCases.Add(conditionalCase);
        block.Settings["input"].InputVariableName = "mainInput";

        var csharp = block.ToCSharp(["mainInput"], new ConfigSettings());

        Assert.Contains("ParseBetweenStrings(data, (string)((object)mainInput).DynamicAsString()", csharp);
    }

    [Fact]
    public void ToCSharp_UsesConditionalInputOverride()
    {
        var block = CreateParseBlock();
        var conditionalCase = block.CreateConditionalCase();
        conditionalCase.Settings["input"].InputVariableName = "caseInput";
        conditionalCase.InputOverridden = true;
        block.ConditionalCases.Add(conditionalCase);

        var csharp = block.ToCSharp(["caseInput"], new ConfigSettings());

        Assert.Contains("ParseBetweenStrings(data, (string)((object)caseInput).DynamicAsString()", csharp);
    }

    [Fact]
    public void FromLc_InheritsMainInputWhenConditionalInputIsNotDeclared()
    {
        var script = """
              input = @mainInput
              MODE:LR
              => VAR @parseOutput
              CASE "Condition 1" OR
                CASEMODE LR
              ENDCASE
            """;
        var lineNumber = 0;
        var parsed = CreateParseBlock();
        parsed.FromLC(ref script, ref lineNumber);

        Assert.Single(parsed.ConditionalCases);
        Assert.False(parsed.ConditionalCases[0].InputOverridden);
        Assert.Equal("mainInput", parsed.ConditionalCases[0].Settings["input"].InputVariableName);
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
        Assert.True(parsed.ConditionalCases[0].InputOverridden);
        Assert.Equal("caseInput", parsed.ConditionalCases[0].Settings["input"].InputVariableName);
    }

    [Fact]
    public void FromLc_TreatsLegacyDefaultConditionalInputAsInherited()
    {
        var script = """
              input = @input.DATA
              caseSensitive = False
              SAFE
              MODE:LR
              => VAR @basicdata
              CASE "Condition 1" OR
                CASEMODE LR
                input = @data.SOURCE
                rightDelim = "END"
                caseSensitive = False
                STRINGKEY @input.DATA Contains "2FA:"
              ENDCASE
            """;
        var lineNumber = 0;
        var parsed = CreateParseBlock();
        parsed.FromLC(ref script, ref lineNumber);

        var conditionalCase = Assert.Single(parsed.ConditionalCases);
        Assert.False(conditionalCase.InputOverridden);
        Assert.Equal("input.DATA", conditionalCase.Settings["input"].InputVariableName);

        var csharp = parsed.ToCSharp(["input.DATA"], new ConfigSettings());

        Assert.Contains("ParseBetweenStrings(data, (string)(input.DATA)", csharp);
        Assert.DoesNotContain("data.SOURCE", csharp);
    }

    [Fact]
    public void SyncInheritedConditionalInput_PreservesUserChangedConditionalInputToDescriptorDefault()
    {
        var block = CreateParseBlock();
        block.Settings["input"].InputVariableName = "input.DATA";
        var conditionalCase = block.CreateConditionalCase();
        block.ConditionalCases.Add(conditionalCase);

        conditionalCase.Settings["input"].InputVariableName = "data.SOURCE";

        block.SyncInheritedConditionalInput(conditionalCase);

        Assert.True(conditionalCase.InputOverridden);
        Assert.True(conditionalCase.InputOverrideExplicitlySet);

        var csharp = block.ToCSharp(["input.DATA"], new ConfigSettings());

        Assert.Contains("ParseBetweenStrings(data, (string)(data.SOURCE)", csharp);
    }

    [Fact]
    public void FromLc_PreservesExplicitDefaultConditionalInputOverride()
    {
        var script = """
              input = @input.DATA
              MODE:LR
              => VAR @basicdata
              CASE "Condition 1" OR
                CASEMODE LR
                INPUTOVERRIDE
                input = @data.SOURCE
              ENDCASE
            """;
        var lineNumber = 0;
        var parsed = CreateParseBlock();
        parsed.FromLC(ref script, ref lineNumber);

        var conditionalCase = Assert.Single(parsed.ConditionalCases);
        Assert.True(conditionalCase.InputOverridden);
        Assert.True(conditionalCase.InputOverrideExplicitlySet);
        Assert.Equal("data.SOURCE", conditionalCase.Settings["input"].InputVariableName);

        var csharp = parsed.ToCSharp(["input.DATA"], new ConfigSettings());

        Assert.Contains("ParseBetweenStrings(data, (string)(data.SOURCE)", csharp);
    }

    private static ParseBlockInstance CreateParseBlock()
        => new(new ParseBlockDescriptor());
}
