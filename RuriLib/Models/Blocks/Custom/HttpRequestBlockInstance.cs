using RuriLib.Functions.Http;
using RuriLib.Functions.Http.Options;
using RuriLib.Models.Blocks.Custom.HttpRequest;
using RuriLib.Models.Blocks.Settings;
using System;
using System.Text.RegularExpressions;

namespace RuriLib.Models.Blocks.Custom;

public partial class HttpRequestBlockInstance(HttpRequestBlockDescriptor descriptor) : BlockInstance(descriptor)
{
    private const string LegacyHttpCloakPresetSettingName = "httpCloakPreset";

    public RequestParams RequestParams { get; set; } = new StandardRequestParams();

    public bool Safe { get; set; }

    private void NormalizeLegacySettings()
    {
        Settings.Remove(LegacyHttpCloakPresetSettingName);

        if (Settings.TryGetValue("httpLibrary", out var setting)
            && setting.FixedSetting is EnumSetting enumSetting
            && string.Equals(enumSetting.Value, "HttpCloak", StringComparison.OrdinalIgnoreCase))
        {
            enumSetting.Value = HttpLibrary.TlsClient.ToString();
        }
    }

    private static bool IsLegacyHttpCloakPresetSetting(string line)
        => line.StartsWith(LegacyHttpCloakPresetSettingName, StringComparison.OrdinalIgnoreCase);

    private static string RewriteLegacyHttpLibrarySetting(string line)
        => line.StartsWith("httpLibrary", StringComparison.OrdinalIgnoreCase)
            ? Regex.Replace(line, "\\bHttpCloak\\b", HttpLibrary.TlsClient.ToString(), RegexOptions.IgnoreCase)
            : line;

    [GeneratedRegex("TYPE:([A-Z]+)")]
    private static partial Regex MyRegex();

    [GeneratedRegex("CONTENT:([A-Z]+)")]
    private static partial Regex MyRegex1();
}
