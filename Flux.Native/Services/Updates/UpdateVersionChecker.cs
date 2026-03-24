using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Flux.Native.Services;

internal sealed class UpdateVersionChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/ocim128/OB2-Edited/releases/latest";
    private const string UserAgent = "Flux-Native-Updater/1.0";

    public async Task<AvailableUpdateInfo?> CheckForUpdateAsync()
    {
        using var httpClient = CreateClient(TimeSpan.FromSeconds(30));
        using var response = await GetWithRetryAsync(httpClient, LatestReleaseUrl).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to check for updates. Status code: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var releaseInfo = JsonConvert.DeserializeObject<UpdateReleaseInfo>(json)
            ?? throw new InvalidOperationException("Failed to parse release information.");

        var latestVersion = releaseInfo.TagName.TrimStart('v');
        var currentVersion = GetCurrentVersion();

        if (string.IsNullOrEmpty(currentVersion) || currentVersion == "Unknown")
        {
            throw new InvalidOperationException("Could not determine current application version.");
        }

        if (Version.Parse(latestVersion) <= Version.Parse(currentVersion))
        {
            return null;
        }

        return new AvailableUpdateInfo
        {
            CurrentVersion = currentVersion,
            LatestVersion = latestVersion,
            ReleaseInfo = releaseInfo
        };
    }

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var httpClient = new HttpClient { Timeout = timeout };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return httpClient;
    }

    private static async Task<HttpResponseMessage> GetWithRetryAsync(HttpClient httpClient, string url, int attempts = 3)
    {
        var delay = 1000;

        for (var i = 1; i <= attempts; i++)
        {
            try
            {
                var response = await httpClient.GetAsync(url).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                if ((int)response.StatusCode is >= 500 and < 600)
                {
                    response.Dispose();
                    await Task.Delay(delay).ConfigureAwait(false);
                    delay *= 2;
                    continue;
                }

                return response;
            }
            catch (TaskCanceledException) when (i < attempts)
            {
                await Task.Delay(delay).ConfigureAwait(false);
                delay *= 2;
            }
            catch (HttpRequestException) when (i < attempts)
            {
                await Task.Delay(delay).ConfigureAwait(false);
                delay *= 2;
            }
        }

        return await httpClient.GetAsync(url).ConfigureAwait(false);
    }

    private static string GetCurrentVersion()
    {
        var currentVersionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
        if (File.Exists(currentVersionPath))
        {
            try
            {
                return File.ReadAllText(currentVersionPath).Trim();
            }
            catch (Exception ioEx)
            {
                Debug.WriteLine($"Failed reading version.txt: {ioEx.Message}");
            }
        }

        return "Unknown";
    }
}
