using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RuriLib.Helpers;
using RuriLib.Models.Configs;
using RuriLib.Tests.Infrastructure;
using Xunit;

namespace RuriLib.Tests.Helpers;

public class ConfigPackerRoundtripTests
{
    [Fact]
    public async Task Pack_Unpack_LoliCodeMode_PreservesScript()
    {
        var config = new Config { Id = "test-id" };
        config.Mode = ConfigMode.LoliCode;
        config.LoliCodeScript = "LOG \"hello\"\n";
        config.Metadata.Name = "Test Config";

        var bytes = await ConfigPacker.PackAsync(config);

        await using var ms = new MemoryStream(bytes);
        var loaded = await ConfigPacker.UnpackAsync(ms);

        Assert.Equal("Test Config", loaded.Metadata.Name);
        Assert.Equal("LOG \"hello\"\n", loaded.LoliCodeScript);
        Assert.Equal(ConfigMode.LoliCode, loaded.Mode);
    }

    [Fact]
    public async Task Pack_Unpack_StackMode_PreservesLoliCodeScript()
    {
        // Stack mode with an empty Stack must keep the existing LoliCodeScript
        // instead of overwriting it with empty transpiled output.
        var config = new Config { Id = "test-id" };
        config.Mode = ConfigMode.Stack;
        config.LoliCodeScript = "LOG \"hello\"\n";
        config.Metadata.Name = "Stack Config";

        var bytes = await ConfigPacker.PackAsync(config);

        await using var ms = new MemoryStream(bytes);
        var loaded = await ConfigPacker.UnpackAsync(ms);

        Assert.Equal("Stack Config", loaded.Metadata.Name);
        Assert.Equal("LOG \"hello\"\n", loaded.LoliCodeScript);
    }
}
