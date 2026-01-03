using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using RuriLib.Models.Settings;

namespace RuriLib.Helpers.Playwright
{
    /// <summary>
    /// Centralizes default tweaks applied before launching Playwright browsers.
    /// Ensures Firefox runs in software mode (no GPU) and sandbox restrictions are disabled.
    /// </summary>
    public static class PlaywrightLaunchConfigurator
    {
        private static readonly string[] CrossPlatformSandboxFlags = { "--no-sandbox" };
        private static readonly string[] NonWindowsSandboxFlags = { "--disable-setuid-sandbox", "--disable-dev-shm-usage" };

        private static readonly IReadOnlyDictionary<string, string> FirefoxSafeEnvironment =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MOZ_CRASHREPORTER_DISABLE"] = "1",
                ["MOZ_DISABLE_GFX_SANITY"] = "1",
                ["MOZ_WEBRENDER"] = "0",
                ["MOZ_ACCELERATED"] = "0",
                ["MOZ_ENABLE_WAYLAND"] = "0",
                ["MOZ_DISABLE_CONTENT_SANDBOX"] = "1"
            };

        private static readonly IReadOnlyDictionary<string, object> FirefoxSafePreferences =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["browser.tabs.remote.autostart"] = false,
                ["dom.ipc.processCount"] = 1,
                ["gfx.canvas.azure.accelerated"] = false,
                ["gfx.webrender.all"] = false,
                ["gfx.webrender.enabled"] = false,
                ["layers.acceleration.disabled"] = true,
                ["layers.acceleration.force-enabled"] = false,
                ["media.hardware-video-decoding.enabled"] = false,
                ["media.ffmpeg.vaapi.enabled"] = false
            };

        private static readonly string[] FirefoxIncompatibleFlags = { "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage" };

        /// <summary>
        /// Makes sure sandbox-disabling flags are always present.
        /// </summary>
        public static void EnsureSandboxFlags(ICollection<string> args, PlaywrightBrowserType browserType)
        {
            if (args == null)
            {
                return;
            }

            foreach (var flag in GetSandboxFlagsForPlatform(browserType))
            {
                if (string.IsNullOrWhiteSpace(flag))
                {
                    continue;
                }

                var normalizedFlag = flag.Trim();
                var hasFlag = args.Any(arg =>
                    !string.IsNullOrWhiteSpace(arg) &&
                    string.Equals(arg.Trim(), normalizedFlag, StringComparison.OrdinalIgnoreCase));

                if (!hasFlag)
                {
                    args.Add(normalizedFlag);
                }
            }
        }

        /// <summary>
        /// Adds stealth flags to Chromium browsers to avoid detection.
        /// </summary>
        public static void EnsureChromiumStealthFlags(ICollection<string> args, PlaywrightBrowserType browserType)
        {
            if (browserType != PlaywrightBrowserType.Chromium || args == null)
            {
                return;
            }

            var stealthFlags = new[]
            {
                "--disable-blink-features=AutomationControlled",
                "--disable-infobars",
                "--disable-background-networking",
                "--disable-backgrounding-occluded-windows",
                "--disable-breakpad",
                "--disable-component-update",
                "--disable-default-apps",
                "--disable-dev-shm-usage",
                "--disable-extensions-file-access-check",
                "--disable-features=TranslateUI,BlinkGenPropertyTrees,ImprovedCookieControls,LazyFrameLoading,GlobalMediaControls,DestroyProfileOnBrowserClose,MediaRouter,DialMediaRouteProvider,AcceptCHFrame,AutoExpandDetailsElement,CertificateTransparencyComponentUpdater,AvoidUnnecessaryBeforeUnloadCheckSync,Translate",
                "--disable-hang-monitor",
                "--disable-ipc-flooding-protection",
                "--disable-popup-blocking",
                "--disable-prompt-on-repost",
                "--disable-renderer-backgrounding",
                "--disable-sync",
                "--enable-features=NetworkService,NetworkServiceInProcess",
                "--force-color-profile=srgb",
                "--metrics-recording-only",
                "--no-first-run",
                "--password-store=basic",
                "--use-mock-keychain",
                "--export-tagged-pdf",
                "--hide-scrollbars",
                "--mute-audio"
            };

            foreach (var flag in stealthFlags)
            {
                var hasFlag = args.Any(arg =>
                    !string.IsNullOrWhiteSpace(arg) &&
                    string.Equals(arg.Trim(), flag, StringComparison.OrdinalIgnoreCase));

                if (!hasFlag)
                {
                    args.Add(flag);
                }
            }
        }

        private static IEnumerable<string> GetSandboxFlagsForPlatform(PlaywrightBrowserType browserType)
        {
            if (browserType != PlaywrightBrowserType.Chromium)
            {
                yield break;
            }

            foreach (var flag in CrossPlatformSandboxFlags)
            {
                yield return flag;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach (var flag in NonWindowsSandboxFlags)
                {
                    yield return flag;
                }
            }
        }

        /// <summary>
        /// Applies Firefox-specific preferences and environment variables to disable GPU usage.
        /// </summary>
        public static void ApplyFirefoxSafeDefaults(BrowserTypeLaunchOptions options)
        {
            if (options == null)
            {
                return;
            }

            options.Env = MergeEnvironment(options.Env);
            options.FirefoxUserPrefs = MergeFirefoxPrefs(options.FirefoxUserPrefs);
        }

        /// <summary>
        /// Applies Firefox-specific preferences and environment variables to disable GPU usage.
        /// </summary>
        public static void ApplyFirefoxSafeDefaults(BrowserTypeLaunchPersistentContextOptions options)
        {
            if (options == null)
            {
                return;
            }

            options.Env = MergeEnvironment(options.Env);
            options.FirefoxUserPrefs = MergeFirefoxPrefs(options.FirefoxUserPrefs);
        }

        private static Dictionary<string, string> MergeEnvironment(IEnumerable<KeyValuePair<string, string>> current)
        {
            var env = current == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase);

            foreach (var pair in FirefoxSafeEnvironment)
            {
                env[pair.Key] = pair.Value;
            }

            return env;
        }

        private static Dictionary<string, object> MergeFirefoxPrefs(IEnumerable<KeyValuePair<string, object>> current)
        {
            var prefs = current == null
                ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object>(current, StringComparer.OrdinalIgnoreCase);

            foreach (var pair in FirefoxSafePreferences)
            {
                prefs[pair.Key] = pair.Value;
            }

            return prefs;
        }

        public static void StripIncompatibleFlags(IList<string> args, PlaywrightBrowserType browserType)
        {
            if (args == null || browserType != PlaywrightBrowserType.Firefox)
            {
                return;
            }

            for (var i = args.Count - 1; i >= 0; i--)
            {
                var arg = args[i];
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                foreach (var invalid in FirefoxIncompatibleFlags)
                {
                    if (arg.Trim().Equals(invalid, StringComparison.OrdinalIgnoreCase))
                    {
                        args.RemoveAt(i);
                        break;
                    }
                }
            }
        }
    }
}
