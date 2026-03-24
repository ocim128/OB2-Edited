using Newtonsoft.Json;

namespace Flux.Native.Services;

internal sealed class UpdateReleaseInfo
{
    [JsonProperty("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonProperty("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonProperty("assets")]
    public UpdateAsset[] Assets { get; set; } = [];
}

internal sealed class UpdateAsset
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonProperty("size")]
    public long Size { get; set; }
}

internal sealed class AvailableUpdateInfo
{
    public required string CurrentVersion { get; init; }
    public required string LatestVersion { get; init; }
    public required UpdateReleaseInfo ReleaseInfo { get; init; }
}

internal readonly record struct UpdateAssetSelection(string? DownloadUrl, long FileSize);
