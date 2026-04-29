using System;
using System.Collections.Generic;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Parameters;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Configs;

namespace RuriLib.Tests.Functions.Playwright;

public class PlaywrightCookieAutoBlockTests
{
    [Fact]
    public void VoidAutoBlock_WithFixedVariableName_SyncsObjectBackToLocalVariable()
    {
        var descriptor = new AutoBlockDescriptor
        {
            Id = "PlaywrightGetCookie",
            Name = "Get Cookie",
            Async = true,
            Parameters = new Dictionary<string, BlockParameter>
            {
                ["cookieName"] = new StringParameter("cookieName"),
                ["variableName"] = new StringParameter("variableName", "cookieValue")
            },
            OriginalParameterTypes = new Dictionary<string, Type>
            {
                ["cookieName"] = typeof(string),
                ["variableName"] = typeof(string)
            }
        };

        var block = new AutoBlockInstance(descriptor);
        block.GetFixedSetting<StringSetting>("cookieName").Value = "auth_token";
        block.GetFixedSetting<StringSetting>("variableName").Value = "auth";

        var transpiled = block.ToCSharp(["auth"], new ConfigSettings());
        var methodCall = "await PlaywrightGetCookie(data, \"auth_token\", \"auth\").ConfigureAwait(false);";
        var syncLine = "auth = data.Objects.ContainsKey(\"auth\") ? data.Objects[\"auth\"] : RuriLib.Models.NullDynamic.Instance;";

        Assert.Contains(methodCall, transpiled, StringComparison.Ordinal);
        Assert.Contains(syncLine, transpiled, StringComparison.Ordinal);
        Assert.True(
            transpiled.IndexOf(methodCall, StringComparison.Ordinal) <
            transpiled.IndexOf(syncLine, StringComparison.Ordinal));
    }
}
