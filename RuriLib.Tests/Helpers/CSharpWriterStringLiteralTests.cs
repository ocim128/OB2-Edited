using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RuriLib.Helpers.Blocks;
using RuriLib.Helpers.CSharp;
using RuriLib.Helpers.Transpilers;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Configs;

namespace RuriLib.Tests.Helpers;

/// <summary>
/// String settings (e.g. the value of a ConstantString block) can contain newlines because the
/// UI exposes them as multi-line inputs. They must be emitted as single-line escaped C# literals,
/// otherwise the generated script fails to compile with "Newline in constant".
/// </summary>
public class CSharpWriterStringLiteralTests
{
    [Fact]
    public void SerializeString_MultiLineValue_EmitsSingleLineLiteral()
    {
        var serialized = CSharpWriter.SerializeString("line1\nline2");

        Assert.DoesNotContain('\n', serialized);
        Assert.Equal("line1\nline2", ParseLiteralValue(serialized));
    }

    [Fact]
    public void FromSetting_MultiLineStringSetting_EmitsCompilableLiteral()
    {
        var setting = new BlockSetting
        {
            FixedSetting = new StringSetting { Value = "a\r\nb\tc\\d\"e" }
        };

        var snippet = CSharpWriter.FromSetting(setting);
        var diagnostics = CSharpSyntaxTree.ParseText($"var x = {snippet};").GetDiagnostics();

        Assert.Empty(diagnostics);
        Assert.Equal("a\r\nb\tc\\d\"e", ParseLiteralValue(snippet));
    }

    [Fact]
    public void SerializeString_ControlCharacters_RoundTrip()
    {
        const string value = "nul\0 nel\u0085 ls\u2028 ps\u2029 bell\u0007";

        var serialized = CSharpWriter.SerializeString(value);

        Assert.Equal(value, ParseLiteralValue(serialized));
    }

    [Fact]
    public void Transpile_ConstantStringBlockWithMultiLineValue_ProducesValidCSharp()
    {
        var descriptor = new DescriptorsRepository().GetAs<AutoBlockDescriptor>("ConstantString");
        var block = new AutoBlockInstance(descriptor);
        ((StringSetting)block.Settings["value"].FixedSetting).Value = "line1\nline2";

        var script = Stack2CSharpTranspiler.Transpile([block], new ConfigSettings());

        var errors = CSharpSyntaxTree.ParseText(script).GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error);

        Assert.Empty(errors);
    }

    private static string ParseLiteralValue(string literal)
        => ((LiteralExpressionSyntax)SyntaxFactory.ParseExpression(literal)).Token.ValueText;
}
