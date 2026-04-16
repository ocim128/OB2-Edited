using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RuriLib.Functions.Http.Options;
using RuriLib.Helpers.CSharp;
using RuriLib.Logging;
using RuriLib.Models.Configs;
using RuriLib.Models.Debugger;
using RuriLib.Services;
using RuriLib.Tests.Infrastructure;

namespace RuriLib.Tests.Debugger;

public class ConfigDebuggerHttpRequestTests
{
    [Theory]
    [InlineData(HttpLibrary.SystemNet)]
    [InlineData(HttpLibrary.RuriLibHttp)]
    public async Task Run_HttpRequestScript_CompletesWithFreshAndReloadedCache(HttpLibrary httpLibrary)
    {
        TestAssemblyResolver.EnsureRegistered();
        await using var server = await TestHttpServer.StartAsync("OK", expectedRequests: 2);
        using var workspace = new TestWorkspace();
        var settingsService = CreateSettingsService(workspace.RootPath);
        var script = CreateHttpRequestScript(server.Uri, httpLibrary);

        ScriptBuilder.ClearCache();

        await RunDebuggerAndAssertAsync(script, settingsService, expectedBody: "OK");

        // Force the next run to reload through the compiled-script cache path instead of the in-memory cache.
        ScriptBuilder.ClearCache();

        await RunDebuggerAndAssertAsync(script, settingsService, expectedBody: "OK");
    }

    [Theory]
    [InlineData(HttpLibrary.SystemNet)]
    [InlineData(HttpLibrary.RuriLibHttp)]
    public async Task Run_HttpRequestScript_WithDuplicateHeaderCasing_DoesNotThrow(HttpLibrary httpLibrary)
    {
        TestAssemblyResolver.EnsureRegistered();
        await using var server = await TestHttpServer.StartAsync("OK", expectedRequests: 1);
        using var workspace = new TestWorkspace();
        var settingsService = CreateSettingsService(workspace.RootPath);
        var script = CreateHttpRequestScript(
            server.Uri,
            httpLibrary,
            @"new Dictionary<string, string>
        {
            [""User-Agent""] = ""agent-one"",
            [""user-agent""] = ""agent-two""
        }");

        await RunDebuggerAndAssertAsync(script, settingsService, expectedBody: "OK");
    }

    private static async Task RunDebuggerAndAssertAsync(
        string script,
        RuriLibSettingsService settingsService,
        string expectedBody)
    {
        var logger = new BotLogger();
        var debugger = new ConfigDebugger(CreateConfig(script), CreateDebuggerOptions(), logger)
        {
            RuriLibSettings = settingsService
        };

        await debugger.Run();

        var body = debugger.Variables.Single(v => v.Name == "responseBody").AsString();
        var statusCode = debugger.Variables.Single(v => v.Name == "responseCode").AsInt();
        var status = debugger.Variables.Single(v => v.Name == "botStatus").AsString();

        Assert.Equal(expectedBody, body);
        Assert.Equal(200, statusCode);
        Assert.Equal("NONE", status);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("ERROR", StringComparison.OrdinalIgnoreCase));
    }

    private static Config CreateConfig(string script) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Mode = ConfigMode.CSharp,
        CSharpScript = script
    };

    private static DebuggerOptions CreateDebuggerOptions() => new()
    {
        TestData = "test-data",
        WordlistType = "Default",
        UseProxy = false
    };

    private static RuriLibSettingsService CreateSettingsService(string rootPath)
    {
        var settings = new RuriLibSettingsService(rootPath);
        settings.RuriLibSettings.GeneralSettings.VerboseMode = true;
        return settings;
    }

    private static string CreateHttpRequestScript(
        Uri uri,
        HttpLibrary httpLibrary,
        string customHeadersExpression = "new Dictionary<string, string>()") =>
$@"data.ExecutingBlock(""Http Request"");
await RuriLib.Blocks.Requests.Http.Methods.HttpRequestStandard(
    data,
    new StandardHttpRequestOptions
    {{
        Url = ""{uri}"",
        Method = RuriLib.Functions.Http.HttpMethod.GET,
        HttpLibrary = HttpLibrary.{httpLibrary},
        AutoRedirect = true,
        MaxNumberOfRedirects = 8,
        ReadResponseContent = true,
        TimeoutMilliseconds = 15000,
        HttpVersion = ""1.1"",
        Content = string.Empty,
        ContentType = ""application/x-www-form-urlencoded"",
        CustomCookies = new Dictionary<string, string>(),
        CustomHeaders = {customHeadersExpression}
    }}).ConfigureAwait(false);

string responseBody = data.SOURCE;
int responseCode = data.RESPONSECODE;
string botStatus = data.STATUS;";
}
