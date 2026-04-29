using RuriLib.Functions.Puppeteer;
using RuriLib.Helpers.Blocks;
using RuriLib.Helpers.LoliCode;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Parameters;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Tests.Infrastructure;
using System.IO;

namespace RuriLib.Tests.Functions.Playwright;

public class PlaywrightElementBlockDescriptorTests
{
    [Theory]
    [InlineData("PlaywrightHoverElement")]
    [InlineData("PlaywrightFocusElement")]
    public void ElementDescriptor_DefaultsToSelectorMode_AndKeepsSelectorSetting(string descriptorId)
    {
        TestAssemblyResolver.EnsureRegistered();
        var repository = new DescriptorsRepository();

        var descriptor = repository.GetAs<AutoBlockDescriptor>(descriptorId);

        var findBy = Assert.IsType<EnumParameter>(descriptor.Parameters["findBy"]);
        Assert.Equal(typeof(FindElementBy), findBy.EnumType);
        Assert.Equal(FindElementBy.Selector.ToString(), findBy.DefaultValue);

        var selector = Assert.IsType<StringParameter>(descriptor.Parameters["selector"]);
        Assert.Equal(string.Empty, selector.DefaultValue);
    }

    [Theory]
    [InlineData("PlaywrightHoverElement")]
    [InlineData("PlaywrightFocusElement")]
    public void ElementBlock_FromLc_PreservesOldSelectorScripts(string descriptorId)
    {
        TestAssemblyResolver.EnsureRegistered();
        var repository = new DescriptorsRepository();
        var descriptor = repository.GetAs<AutoBlockDescriptor>(descriptorId);
        var block = new ParserProbeBlockInstance(descriptor);
        var script = """
              selector = ".cta"
              timeoutSeconds = 12
            """;

        block.ParseSettings(script);

        Assert.Equal(FindElementBy.Selector.ToString(), block.GetFixedSetting<EnumSetting>("findBy").Value);
        Assert.Equal(".cta", block.GetFixedSetting<StringSetting>("selector").Value);
        Assert.Equal(0, block.GetFixedSetting<IntSetting>("index").Value);
        Assert.Equal(12, block.GetFixedSetting<IntSetting>("timeoutSeconds").Value);
    }

    private sealed class ParserProbeBlockInstance(AutoBlockDescriptor descriptor) : BlockInstance(descriptor)
    {
        public void ParseSettings(string script)
        {
            using var reader = new StringReader(script);

            while (reader.ReadLine() is { } line)
            {
                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                LoliCodeParser.ParseSetting(ref line, Settings, Descriptor);
            }
        }
    }
}
