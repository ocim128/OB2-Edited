using Microsoft.Playwright;
using RuriLib.Helpers.Playwright;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Settings;
using RuriLib.Providers.Playwright;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Playwright.Browser
{
    public static partial class Methods
    {
        private static string? GetExecutablePath(PlaywrightBrowserType browserType, BotData data)
        {
            var provider = data.Providers.PlaywrightBrowser;
            var configuredPath = browserType switch
            {
                PlaywrightBrowserType.Chromium => provider.ChromiumBinaryLocation,
                PlaywrightBrowserType.Firefox => provider.FirefoxBinaryLocation,
                PlaywrightBrowserType.Webkit => provider.WebkitBinaryLocation,
                _ => null
            };

            return TryResolveExecutableOverride(configuredPath, browserType, data);
        }

        private static PlaywrightBrowserType ValidateBrowserType(PlaywrightBrowserType browserType, string? executablePath, BotData data)
        {
            if (browserType == PlaywrightBrowserType.Webkit && !string.IsNullOrEmpty(executablePath))
            {
                var executableName = Path.GetFileNameWithoutExtension(executablePath).ToLowerInvariant();
                if (executableName.Contains("firefox") || executableName.Contains("camoufox") || executableName.Contains("librewolf"))
                {
                    data.Logger.Log($"⚠️ Detected Firefox-based browser ({executableName}) configured as Webkit. Switching to Firefox browser type.", LogColors.Orange);
                    return PlaywrightBrowserType.Firefox;
                }
            }

            return browserType;
        }

        private static async Task<IBrowser> LaunchBrowserWithRetry(IPlaywright playwright, PlaywrightBrowserType browserType, BrowserTypeLaunchOptions options, BotData data)
        {
            return browserType switch
            {
                PlaywrightBrowserType.Chromium => await playwright.Chromium.LaunchAsync(options).ConfigureAwait(false),
                PlaywrightBrowserType.Firefox => await playwright.Firefox.LaunchAsync(options).ConfigureAwait(false),
                PlaywrightBrowserType.Webkit => await playwright.Webkit.LaunchAsync(options).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unsupported browser type: {browserType}")
            };
        }

        private static string? TryResolveExecutableOverride(string? configuredPath, PlaywrightBrowserType browserType, BotData data)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return null;
            }

            var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            if (!Path.IsPathRooted(expandedPath))
            {
                expandedPath = Path.GetFullPath(expandedPath);
            }

            if (File.Exists(expandedPath))
            {
                return expandedPath;
            }

            if (Directory.Exists(expandedPath))
            {
                var executableCandidates = GetBrowserExecutableCandidates(browserType);
                foreach (var candidate in executableCandidates)
                {
                    var match = Directory.EnumerateFiles(expandedPath, candidate, SearchOption.AllDirectories).FirstOrDefault();
                    if (match != null)
                    {
                        data.Logger.Log($"Resolved {browserType} executable to '{match}'", LogColors.MediumPurple);
                        return match;
                    }
                }
            }

            data.Logger.Log($"Configured {browserType} executable not found at '{expandedPath}'. Falling back to Playwright managed runtime at '{PlaywrightRuntimeService.ActiveRuntimePath}'.", LogColors.Yellow);
            return null;
        }

        private static IEnumerable<string> GetBrowserExecutableCandidates(PlaywrightBrowserType browserType) =>
            browserType switch
            {
                PlaywrightBrowserType.Chromium => new[] { "chrome.exe", "msedge.exe", "chromium.exe" },
                PlaywrightBrowserType.Firefox => new[] { "firefox.exe", "librewolf.exe", "waterfox.exe" },
                PlaywrightBrowserType.Webkit => new[] { "Playwright.exe" },
                _ => Array.Empty<string>()
            };

        private static void HandleBrowserLaunchError(Exception ex, PlaywrightBrowserType browserType, string? executablePath, BotData data)
        {
            data.Logger.Log("❌ Browser launch failed with error:", LogColors.Red);
            data.Logger.Log($"   Exception Type: {ex.GetType().Name}", LogColors.Red);
            data.Logger.Log($"   Message: {ex.Message}", LogColors.Red);
            if (ex.InnerException != null)
            {
                data.Logger.Log($"   Inner Exception: {ex.InnerException.Message}", LogColors.Red);
            }

            data.Logger.Log("💡 Troubleshooting tips:", LogColors.Yellow);
            data.Logger.Log("   - Verify browser executable path is correct", LogColors.Yellow);
            data.Logger.Log("   - Check if browser supports automation", LogColors.Yellow);
            data.Logger.Log("   - Try using Playwright's built-in browsers", LogColors.Yellow);
        }

        private static async Task InstallFirefoxAddon(string profilePath, string addonPath, BotData data)
        {
            try
            {
                var extensionsDir = Path.Combine(profilePath, "extensions");
                if (!Directory.Exists(extensionsDir))
                {
                    Directory.CreateDirectory(extensionsDir);
                    data.Logger.Log($"Created extensions directory: {extensionsDir}", LogColors.MediumPurple);
                }

                if (File.Exists(addonPath) && Path.GetExtension(addonPath).Equals(".xpi", StringComparison.OrdinalIgnoreCase))
                {
                    var originalFileName = Path.GetFileName(addonPath);
                    var properFileName = GenerateProperAddonFileName(originalFileName);
                    var destinationPath = Path.Combine(extensionsDir, properFileName);
                    File.Copy(addonPath, destinationPath, true);

                    if (!originalFileName.Equals(properFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        data.Logger.Log($"Renamed addon from '{originalFileName}' to '{properFileName}' for proper Firefox recognition", LogColors.MediumPurple);
                    }

                    data.Logger.Log($"Installed Firefox addon: {properFileName}", LogColors.Green);
                }
                else if (Directory.Exists(addonPath))
                {
                    var xpiFiles = Directory.GetFiles(addonPath, "*.xpi", SearchOption.TopDirectoryOnly);
                    if (xpiFiles.Length == 0)
                    {
                        data.Logger.Log($"⚠️ No .xpi files found in directory: {addonPath}", LogColors.Orange);
                        return;
                    }

                    foreach (var xpiFile in xpiFiles)
                    {
                        var originalFileName = Path.GetFileName(xpiFile);
                        var properFileName = GenerateProperAddonFileName(originalFileName);
                        var destinationPath = Path.Combine(extensionsDir, properFileName);
                        File.Copy(xpiFile, destinationPath, true);

                        if (!originalFileName.Equals(properFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            data.Logger.Log($"Renamed addon from '{originalFileName}' to '{properFileName}' for proper Firefox recognition", LogColors.MediumPurple);
                        }

                        data.Logger.Log($"Installed Firefox addon: {properFileName}", LogColors.Green);
                    }
                }
                else
                {
                    data.Logger.Log($"⚠️ Firefox addon path not found or invalid: {addonPath}", LogColors.Orange);
                }
            }
            catch (Exception ex)
            {
                data.Logger.Log($"❌ Failed to install Firefox addon: {ex.Message}", LogColors.Red);
            }
        }

        private static string GenerateProperAddonFileName(string originalFileName)
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
            if (nameWithoutExtension.StartsWith("addon@", StringComparison.OrdinalIgnoreCase) && nameWithoutExtension.Contains('.'))
            {
                return originalFileName;
            }

            var cleanName = nameWithoutExtension
                .ToLowerInvariant()
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty);

            if (string.IsNullOrEmpty(cleanName))
            {
                cleanName = "extension";
            }

            return $"addon@{cleanName}.com.xpi";
        }
    }
}
