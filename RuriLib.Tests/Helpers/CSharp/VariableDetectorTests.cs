using RuriLib.Helpers.CSharp;

namespace RuriLib.Tests.Helpers.CSharp;

/// <summary>
/// <see cref="VariableDetector"/> decides which variables get a
/// <c>dynamic x = NullDynamic.Instance;</c> declaration in every transpiled config.
/// A false positive declares a name that must not exist; a false negative emits a
/// reference to a variable that was never declared, which fails Roslyn compilation.
/// </summary>
public class VariableDetectorTests
{
    [Theory]
    [InlineData("data", null)]
    [InlineData("globals", null)]
    [InlineData("input", null)]
    [InlineData("_private", null)]
    [InlineData("9lives", null)]
    [InlineData("", null)]
    public void ExtractBaseVariableName_ReturnsNullForNonVariables(string expression, string? expected)
        => Assert.Equal(expected, VariableDetector.ExtractBaseVariableName(expression));

    [Theory]
    [InlineData("user", "user")]
    [InlineData("user.Name", "user")]
    [InlineData("user[0]", "user")]
    [InlineData("user.profile[0].Email", "user")]
    [InlineData("  padded.prop  ", "padded")]
    public void ExtractBaseVariableName_StripsMemberAccessAndIndexers(string expression, string expected)
        => Assert.Equal(expected, VariableDetector.ExtractBaseVariableName(expression));

    [Fact]
    public void DetectFromInterpolatedString_CollectsBaseNamesOfInterpolatedExpressions()
    {
        var detected = VariableDetector.DetectFromInterpolatedString("<user.Name>-<token>");

        Assert.Equal(new[] { "token", "user" }, detected.OrderBy(v => v));
    }

    [Fact]
    public void DetectFromInterpolatedString_IgnoresBuiltInAndReservedObjects()
    {
        var detected = VariableDetector.DetectFromInterpolatedString("<data.SOURCE>-<globals.Key>");

        Assert.Empty(detected);
    }

    [Fact]
    public void DetectFromLoliCodeStatement_FindsAtPrefixedAndLoliInterpolatedVariables()
    {
        var detected = VariableDetector.DetectFromLoliCodeStatement(
            "value = $\"<captured>\"\nLOG @logged VALUE");

        Assert.Contains("captured", detected);
        Assert.Contains("logged", detected);
    }

    [Fact]
    public void DetectFromLoliCodeStatement_DoesNotTreatLoliCodeKeywordsAsVariables()
    {
        var detected = VariableDetector.DetectFromLoliCodeStatement("IF @flag EqualTo \"x\"\nENDIF");

        Assert.Contains("flag", detected);
        Assert.DoesNotContain("IF", detected);
        Assert.DoesNotContain("ENDIF", detected);
        Assert.DoesNotContain("EqualTo", detected);
    }

    [Fact]
    public void DetectFromLoliCodeStatement_DoesNotTreatKeycheckComparisonTokensAsVariables()
    {
        // LoliCode keycheck comparisons are bare identifiers in a statement, so they must be
        // in the reserved set or every keycheck would emit a bogus variable declaration.
        var detected = VariableDetector.DetectFromLoliCodeStatement(
            "STRINGKEY @myVariable Contains \"abc\"\nINTKEY @code GreaterThan 200");

        Assert.Contains("myVariable", detected);
        Assert.Contains("code", detected);
        Assert.DoesNotContain("Contains", detected);
        Assert.DoesNotContain("GreaterThan", detected);
        Assert.DoesNotContain("STRINGKEY", detected);
        Assert.DoesNotContain("INTKEY", detected);
    }

    [Fact]
    public void GetMissingVariables_ExcludesAlreadyDeclaredVariables()
    {
        var missing = VariableDetector.GetMissingVariables(
            new HashSet<string>(StringComparer.Ordinal) { "known", "unknown" },
            ["known"]);

        Assert.Equal(["unknown"], missing);
    }

    [Fact]
    public void GetMissingVariables_ExcludesBuiltInRootObjectsAndTheirMembers()
    {
        var missing = VariableDetector.GetMissingVariables(
            new HashSet<string>(StringComparer.Ordinal) { "data", "data.SOURCE", "globals", "globals.Key", "input", "input.USER", "real" },
            []);

        Assert.Equal(["real"], missing);
    }

    [Fact]
    public void GetMissingVariables_RejectsNamesDifferingOnlyByCase()
    {
        // Declarations are emitted verbatim into C#, where identifiers are case-sensitive:
        // a variable detected as "Name" must not be satisfied by a declared "name".
        var missing = VariableDetector.GetMissingVariables(
            new HashSet<string>(StringComparer.Ordinal) { "Name" },
            ["name"]);

        Assert.Equal(["Name"], missing);
    }
}
