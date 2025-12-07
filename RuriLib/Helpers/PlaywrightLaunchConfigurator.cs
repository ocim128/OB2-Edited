using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RuriLib.Helpers.Playwright
{
    /// <summary>
    /// Centralizes default tweaks applied before launching Playwright browsers.
    /// Ensures Firefox runs in software mode (no GPU) and sandbox restrictions are disabled.
    /// </summary>
    public static class PlaywrightLaunchConfigurator
    {
        private static readonly string[] RequiredSandboxFlags = new[]
        {
            "--no-sandbox",
            "--disable-setuid-sandbox"
        };

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

        /// <summary>
        /// Makes sure sandbox-disabling flags are always present.
        /// </summary>
        public static void EnsureSandboxFlags(ICollection<string> args)
        {
            if (args == null)
            {
                return;
            }

            foreach (var flag in RequiredSandboxFlags)
            {
                if (!args.Any(arg => arg.Equals(flag, StringComparison.OrdinalIgnoreCase)))
                {
                    args.Add(flag);
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
    }
}
