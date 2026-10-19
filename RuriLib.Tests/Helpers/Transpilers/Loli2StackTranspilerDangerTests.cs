using RuriLib.Helpers.Transpilers;

namespace RuriLib.Tests.Helpers.Transpilers;

/// <summary>
/// <c>Loli2StackTranspiler.IsScriptDangerous</c> backs <c>Config.HasCSharpCode()</c>, which is how the
/// UI decides that a LoliCode config contains raw C# / shell execution (an arbitrary-code surface).
/// A false negative hides a dangerous config; a false positive wrongly flags a pure block config.
/// </summary>
public class Loli2StackTranspilerDangerTests
{
    [Theory]
    [InlineData("BLOCK:ConstantString\n  value = \"hello\"\nENDBLOCK")]
    [InlineData("BLOCK:ConstantString\n  value = \"a\"\nENDBLOCK\n\nBLOCK:ConstantString\n  value = \"b\"\nENDBLOCK")]
    public void IsScriptDangerous_ReturnsFalseForPureBlockScripts(string script)
        => Assert.False(Loli2StackTranspiler.IsScriptDangerous(script));

    [Theory]
    [InlineData("BLOCK:Script\n  code = \"return 1;\"\nENDBLOCK")]
    [InlineData("BLOCK:ShellCommand\n  executable = \"cmd\"\nENDBLOCK")]
    public void IsScriptDangerous_ReturnsTrueForScriptAndShellBlocks(string script)
        => Assert.True(Loli2StackTranspiler.IsScriptDangerous(script));

    [Fact]
    public void IsScriptDangerous_ReturnsTrueForRawLoliCode()
        => Assert.True(Loli2StackTranspiler.IsScriptDangerous("SET @x 1"));

    [Fact]
    public void IsScriptDangerous_ReturnsTrueWhenDangerousBlockFollowsASafeBlock()
    {
        var script = "BLOCK:ConstantString\n  value = \"hello\"\nENDBLOCK\n\nBLOCK:ShellCommand\n  executable = \"cmd\"\nENDBLOCK";

        Assert.True(Loli2StackTranspiler.IsScriptDangerous(script));
    }

    [Fact]
    public void IsScriptDangerous_ReturnsFalseForEmptyOrWhitespace()
    {
        Assert.False(Loli2StackTranspiler.IsScriptDangerous(""));
        Assert.False(Loli2StackTranspiler.IsScriptDangerous("   \r\n\r\n"));
        Assert.False(Loli2StackTranspiler.IsScriptDangerous(null));
    }

    [Fact]
    public void IsScriptDangerous_IgnoresDangerousLookingTextInsideBlockBody()
    {
        // The dangerous block is matched on the BLOCK directive only, so a constant whose
        // value merely contains the word must not be reported as executable code.
        var script = "BLOCK:ConstantString\n  value = \"Script\"\nENDBLOCK";

        Assert.False(Loli2StackTranspiler.IsScriptDangerous(script));
    }
}
