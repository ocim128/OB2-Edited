using RuriLib.Attributes;
using RuriLib.Blocks.Functions.String;
using RuriLib.Helpers.Blocks;
using RuriLib.Models.Blocks;
using RuriLib.Models.Bots;
using RuriLib.Tests.Functions.Http;
using RuriLib.Tests.Infrastructure;
using System.Threading.Tasks;

namespace RuriLib.Tests.Functions.StringFunctions;

public class TranslateLanguageBlockTests
{
    [Fact]
    public void TranslateLanguageDescriptor_IsMarkedAsync()
    {
        var repository = new DescriptorsRepository();

        var descriptor = repository.GetAs<AutoBlockDescriptor>("TranslateLanguage");

        Assert.True(descriptor.Async);
    }

    [Fact]
    public void TaskReturningBlockWrapperDescriptor_IsMarkedAsync()
    {
        var repository = new DescriptorsRepository();
        repository.AddFromExposedMethods(new[] { typeof(TaskReturningBlockWrapperMethods) });

        var descriptor = repository.GetAs<AutoBlockDescriptor>(nameof(TaskReturningBlockWrapperMethods.TaskWrapperDescriptorProbe));

        Assert.True(descriptor.Async);
    }

    [Fact]
    public async Task TranslateLanguageCoreAsync_SendsExpectedRequestAndParsesTranslation()
    {
        await using var server = await TestHttpServer.StartAsync("[[\"late afternoon\",\"id\"]]", expectedRequests: 1);
        using var context = new HttpTransportTestContext();

        var translated = await Methods.TranslateLanguageCoreAsync(context.Data, "senja", "en", "auto", server.Uri);

        Assert.Equal("late afternoon", translated);

        var request = Assert.Single(server.RecordedRequests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/translate_a/t?client=dict-chrome-ex&sl=auto&tl=en&q=senja", request.Path);
        Assert.Equal("application/x-www-form-urlencoded", request.GetHeader("Content-Type"));
        Assert.Equal("1", request.GetHeader("Content-Length"));
        Assert.Equal("0", request.Body);
    }

    [Fact]
    public async Task TranslateLanguageCoreAsync_ReturnsEmptyStringWithoutSendingRequest_WhenInputIsEmpty()
    {
        await using var server = await TestHttpServer.StartAsync("unused", expectedRequests: 1);
        using var context = new HttpTransportTestContext();

        var translated = await Methods.TranslateLanguageCoreAsync(context.Data, string.Empty, "en", "auto", server.Uri);

        Assert.Equal(string.Empty, translated);
        Assert.Empty(server.RecordedRequests);
    }
}

[RuriLib.Attributes.BlockCategory("Test Blocks", "Test blocks", "#000")]
internal static class TaskReturningBlockWrapperMethods
{
    [Block("Descriptor async probe")]
    public static Task<string> TaskWrapperDescriptorProbe(BotData data)
    {
        _ = data;
        return Task.FromResult("ok");
    }
}
