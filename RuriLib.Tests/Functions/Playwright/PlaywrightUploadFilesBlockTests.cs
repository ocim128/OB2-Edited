using RuriLib.Blocks.Playwright.Elements;
using RuriLib.Functions.Puppeteer;
using RuriLib.Helpers.Blocks;
using RuriLib.Logging;
using RuriLib.Models.Blocks;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs;
using RuriLib.Models.Data;
using RuriLib.Models.Environment;
using RuriLib.Services;
using RuriLib.Tests.Infrastructure;
using System.Threading.Tasks;

namespace RuriLib.Tests.Functions.Playwright;

public class PlaywrightUploadFilesBlockTests
{
    [Fact]
    public void Descriptor_ExposesFileListAndOptionalTimeout()
    {
        TestAssemblyResolver.EnsureRegistered();
        var repository = new DescriptorsRepository();

        var descriptor = repository.GetAs<AutoBlockDescriptor>("PlaywrightUploadFiles");

        Assert.True(descriptor.Async);
        Assert.NotNull(descriptor.Parameters["filePaths"]);
        Assert.NotNull(descriptor.Parameters["findBy"]);
        Assert.NotNull(descriptor.Parameters["identifier"]);
        Assert.NotNull(descriptor.Parameters["index"]);
        Assert.NotNull(descriptor.Parameters["timeoutSeconds"]);
    }

    // File validation must run before the block touches the browser session.
    // Otherwise a bad path reports "No page available", hiding the real configuration mistake.
    [Fact]
    public async Task MissingFile_FailsWithPathNotWithMissingBrowser()
    {
        var data = CreateBotData();
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");

        var ex = await Assert.ThrowsAsync<Exception>(() =>
            Methods.PlaywrightUploadFiles(data, FindElementBy.XPath, "//input", 0, [missing]));

        Assert.Contains("do not exist", ex.Message);
        Assert.Contains(missing, ex.Message);
        Assert.DoesNotContain("No page available", ex.Message);
    }

    [Fact]
    public async Task EmptyFileList_FailsBeforeTouchingTheBrowser()
    {
        var data = CreateBotData();

        var ex = await Assert.ThrowsAsync<Exception>(() =>
            Methods.PlaywrightUploadFiles(data, FindElementBy.XPath, "//input", 0, []));

        Assert.Contains("No files were specified", ex.Message);
        Assert.DoesNotContain("No page available", ex.Message);
    }

    private static BotData CreateBotData()
    {
        TestAssemblyResolver.EnsureRegistered();
        var workspace = new TestWorkspace();
        var settings = new RuriLibSettingsService(workspace.RootPath);

        return new BotData(
            new RuriLib.Models.Bots.Providers(settings),
            new ConfigSettings(),
            new BotLogger(),
            new DataLine("test", new WordlistType { Name = "Default" }));
    }
}
