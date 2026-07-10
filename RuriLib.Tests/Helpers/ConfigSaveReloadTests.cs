using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RuriLib.Helpers;
using RuriLib.Helpers.Transpilers;
using RuriLib.Models.Blocks;
using RuriLib.Models.Configs;
using Xunit;

namespace RuriLib.Tests.Helpers;

public class ConfigSaveReloadTests
{
    [Fact]
    public async Task StackMode_WithBlocks_RoundtripsContent()
    {
        var config = new Config { Id = "my-config" };
        config.Mode = ConfigMode.Stack;
        config.Metadata.Name = "My Config";
        config.Stack.Add(new LoliCodeBlockInstance(new LoliCodeBlockDescriptor())
        {
            Script = "LOG \"hello\"\n"
        });

        var bytes = await ConfigPacker.PackAsync(config);

        await using var ms = new MemoryStream(bytes);
        var reloaded = await ConfigPacker.UnpackAsync(ms);

        Assert.False(string.IsNullOrEmpty(reloaded.LoliCodeScript),
            "Reloaded config must keep its script content.");
        Assert.Contains("LOG", reloaded.LoliCodeScript);
    }

    [Fact]
    public async Task StackMode_EmptyStack_PreservesExistingScript()
    {
        // Regression guard: a Stack-mode config whose Stack is temporarily empty
        // at save time must not wipe out previously saved LoliCodeScript content.
        var config = new Config { Id = "my-config" };
        config.Mode = ConfigMode.Stack;
        config.LoliCodeScript = "LOG \"before\"\n";
        config.Stack = new System.Collections.Generic.List<BlockInstance>();

        var bytes = await ConfigPacker.PackAsync(config);

        await using var ms = new MemoryStream(bytes);
        var reloaded = await ConfigPacker.UnpackAsync(ms);

        Assert.Equal("LOG \"before\"\n", reloaded.LoliCodeScript);
    }
}
