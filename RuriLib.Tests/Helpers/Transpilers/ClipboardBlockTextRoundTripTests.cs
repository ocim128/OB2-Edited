using RuriLib.Helpers.Blocks;
using RuriLib.Helpers.Transpilers;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Settings;

namespace RuriLib.Tests.Helpers.Transpilers;

/// <summary>
/// The stacker clipboard copies blocks as canonical LoliCode text — the same form
/// Stack2LoliTranspiler writes for configs: BLOCK:{id} + ToLC + ENDBLOCK for blocks,
/// raw script for LoliCode blocks — and pastes by running that text back through
/// Loli2StackTranspiler. These tests lock the contract behind paste: whatever the
/// copy format writes must come back as equivalent blocks, and blank lines inside a
/// block body must not shred a single block into per-line blocks (that is what
/// broke Ctrl+V paste).
/// </summary>
public class ClipboardBlockTextRoundTripTests
{
    // Mirrors ConfigStackerViewModel.CreateDetailedBlockText (Flux.Native), which cannot be
    // referenced from here; if one side changes format, this test must fail.
    private static string CopyBlockText(BlockInstance block)
        => block is LoliCodeBlockInstance loliBlock
            ? loliBlock.Script ?? string.Empty
            : $"BLOCK:{block.Id}\n{block.ToLC(true)}ENDBLOCK";

    private static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n").TrimEnd('\n');

    [Fact]
    public void AutoBlock_Settings_SurviveClipboardRoundTrip()
    {
        var descriptor = new DescriptorsRepository().GetAs<AutoBlockDescriptor>("ConstantString");
        var block = new AutoBlockInstance(descriptor);
        ((StringSetting)block.Settings["value"].FixedSetting).Value = "hello world";
        block.Safe = true;

        var parsed = Assert.Single(Loli2StackTranspiler.Transpile(CopyBlockText(block)));
        var parsedAuto = Assert.IsAssignableFrom<AutoBlockInstance>(parsed);

        Assert.Equal("hello world", ((StringSetting)parsedAuto.Settings["value"].FixedSetting).Value);
        Assert.True(parsedAuto.Safe);
    }

    [Fact]
    public void LoliCodeBlock_WithBlankLines_StaysOneBlockWithFullScript()
    {
        var block = new LoliCodeBlockInstance(new LoliCodeBlockDescriptor())
        {
            Script = "SET @a \"1\"\n\nIF @a == \"1\"\nLOG @a\nEND"
        };

        var parsed = Assert.Single(Loli2StackTranspiler.Transpile(CopyBlockText(block)));
        var parsedLoli = Assert.IsType<LoliCodeBlockInstance>(parsed);

        Assert.Equal(NormalizeNewlines(block.Script), NormalizeNewlines(parsedLoli.Script));
    }

    [Fact]
    public void CopiedBlockSequence_PasteBackAsSeparateBlocks()
    {
        var first = new LoliCodeBlockInstance(new LoliCodeBlockDescriptor()) { Script = "SET @a \"1\"" };
        var second = new AutoBlockInstance(new DescriptorsRepository().GetAs<AutoBlockDescriptor>("ConstantString"));

        var clipboardText = string.Join(
            Environment.NewLine + Environment.NewLine,
            CopyBlockText(first), CopyBlockText(second));

        var parsed = Loli2StackTranspiler.Transpile(clipboardText);

        Assert.Equal(2, parsed.Count);
        Assert.IsType<LoliCodeBlockInstance>(parsed[0]);
        Assert.IsAssignableFrom<AutoBlockInstance>(parsed[1]);
    }
}
