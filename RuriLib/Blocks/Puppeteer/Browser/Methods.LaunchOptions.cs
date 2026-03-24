using PuppeteerSharp;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RuriLib.Blocks.Puppeteer.Browser;

public static partial class Methods
{
    private static readonly string[] PuppeteerIgnoredDefaultArgs =
    [
        "--enable-blink-features=IdleDetection",
        "--disable-extensions",
        "--disable-component-extensions-with-background-pages"
    ];

    private static readonly List<string> BaseBrowserArgs = new();

    private static readonly JsonSerializerOptions RealBrowserJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static List<string> ParseCommandLineArgs(string commandLine)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return args;
        }

        var currentArg = new StringBuilder();
        var inQuotes = false;
        var escapeNext = false;

        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];

            if (escapeNext)
            {
                currentArg.Append(c);
                escapeNext = false;
            }
            else if (c == '\\' && i + 1 < commandLine.Length && (commandLine[i + 1] == '"' || commandLine[i + 1] == '\\'))
            {
                escapeNext = true;
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (currentArg.Length > 0)
                {
                    args.Add(currentArg.ToString());
                    currentArg.Clear();
                }
            }
            else
            {
                currentArg.Append(c);
            }
        }

        if (currentArg.Length > 0)
        {
            args.Add(currentArg.ToString());
        }

        return args;
    }

    private static string NormalizeExtensionPath(string extensionValue)
    {
        if (string.IsNullOrWhiteSpace(extensionValue))
        {
            return extensionValue;
        }

        var normalizedParts = extensionValue
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => ResolveBrowserPath(part.Trim()));

        return string.Join(",", normalizedParts);
    }

    private static string ResolveBrowserPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var trimmed = path.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed;
        }

        var expanded = Environment.ExpandEnvironmentVariables(trimmed);

        try
        {
            if (Path.IsPathRooted(expanded))
            {
                return Path.GetFullPath(expanded);
            }

            var baseDirectory = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = Directory.GetCurrentDirectory();
            }

            return Path.GetFullPath(Path.Combine(baseDirectory, expanded));
        }
        catch
        {
            return expanded;
        }
    }

    private static List<string> BuildBrowserArguments(BotData data, string extraCmdLineArgs, bool includeDefaultArgs)
    {
        var browserArgs = includeDefaultArgs
            ? new List<string>(BaseBrowserArgs)
            : new List<string>();

        if (!string.IsNullOrWhiteSpace(data.ConfigSettings.BrowserSettings.CommandLineArgs))
        {
            browserArgs.AddRange(ParseCommandLineArgs(data.ConfigSettings.BrowserSettings.CommandLineArgs));
        }

        if (!string.IsNullOrWhiteSpace(extraCmdLineArgs))
        {
            browserArgs.AddRange(ParseCommandLineArgs(extraCmdLineArgs));
        }

        if (includeDefaultArgs)
        {
            browserArgs.RemoveAll(static arg =>
                arg.Contains("--disable-extensions", StringComparison.OrdinalIgnoreCase)
                || arg.Contains("--disable-plugins", StringComparison.OrdinalIgnoreCase)
                || arg.Contains("--disable-default-apps", StringComparison.OrdinalIgnoreCase));

            if (!browserArgs.Any(arg => arg.Equals("--enable-extensions", StringComparison.OrdinalIgnoreCase)))
            {
                browserArgs.Add("--enable-extensions");
            }

            var loadExtensionArgs = browserArgs
                .Where(arg => arg.StartsWith("--load-extension=", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (loadExtensionArgs.Any())
            {
                browserArgs.RemoveAll(arg => arg.StartsWith("--load-extension=", StringComparison.OrdinalIgnoreCase));

                foreach (var loadExtArg in loadExtensionArgs)
                {
                    var extensionPath = loadExtArg.Substring("--load-extension=".Length).Trim('"');
                    var normalizedExtensionPath = NormalizeExtensionPath(extensionPath);

                    browserArgs.Add($"--load-extension=\"{normalizedExtensionPath}\"");

                    if (!browserArgs.Any(arg => arg.StartsWith("--disable-extensions-except=", StringComparison.OrdinalIgnoreCase)))
                    {
                        browserArgs.Add($"--disable-extensions-except=\"{normalizedExtensionPath}\"");
                    }
                }
            }

            if (!browserArgs.Any(arg => arg.Equals("--disable-web-security", StringComparison.OrdinalIgnoreCase)))
            {
                browserArgs.Add("--disable-web-security");
            }

            if (!browserArgs.Any(arg => arg.StartsWith("--disable-features=VizDisplayCompositor", StringComparison.OrdinalIgnoreCase)))
            {
                browserArgs.Add("--disable-features=VizDisplayCompositor");
            }
        }

        if (data.UseProxy && data.Proxy != null)
        {
            var proxyArg = $"--proxy-server={data.Proxy.Type.ToString().ToLower(CultureInfo.CurrentCulture)}://{data.Proxy.Host}:{data.Proxy.Port}";
            if (!browserArgs.Contains(proxyArg, StringComparer.OrdinalIgnoreCase))
            {
                browserArgs.Add(proxyArg);
            }

            if (data.Proxy.NeedsAuthentication)
            {
                var authArg = $"--proxy-auth={data.Proxy.Username}:{data.Proxy.Password}";
                if (!browserArgs.Contains(authArg, StringComparer.OrdinalIgnoreCase))
                {
                    browserArgs.Add(authArg);
                }
            }
        }

        return browserArgs
            .Where(static arg => !string.IsNullOrWhiteSpace(arg))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static RealBrowserLaunchOptions CreateRealBrowserLaunchOptions(BotData data, IReadOnlyCollection<string> browserArgs) =>
        new()
        {
            Args = browserArgs.ToArray(),
            Headless = data.ConfigSettings.BrowserSettings.Headless,
            Turnstile = true,
            DisableXvfb = true,
            IgnoreAllFlags = false,
            ConnectOption = new RealBrowserConnectOptions
            {
                DefaultViewport = null,
                IgnoreHTTPSErrors = data.ConfigSettings.BrowserSettings.IgnoreHttpsErrors
            },
            CustomConfig = CreateRealBrowserCustomConfig(data),
            Proxy = CreateRealBrowserProxyOptions(data)
        };

    private static LaunchOptions CreatePuppeteerLaunchOptions(BotData data, IReadOnlyCollection<string> browserArgs) =>
        new()
        {
            Headless = data.ConfigSettings.BrowserSettings.Headless,
            Args = browserArgs.ToArray(),
            AcceptInsecureCerts = data.ConfigSettings.BrowserSettings.IgnoreHttpsErrors,
            SlowMo = 0,
            Timeout = 30000,
            ExecutablePath = data.Providers.PuppeteerBrowser.ChromeBinaryLocation,
            DefaultViewport = null,
            IgnoredDefaultArgs = PuppeteerIgnoredDefaultArgs
        };

    private static RealBrowserCustomConfig CreateRealBrowserCustomConfig(BotData data)
    {
        var chromePath = data.Providers.PuppeteerBrowser.ChromeBinaryLocation;
        return string.IsNullOrWhiteSpace(chromePath)
            ? null
            : new RealBrowserCustomConfig { ChromePath = chromePath };
    }

    private static RealBrowserProxyOptions CreateRealBrowserProxyOptions(BotData data)
    {
        if (!data.UseProxy || data.Proxy == null)
        {
            return null;
        }

        return new RealBrowserProxyOptions
        {
            Host = data.Proxy.Host,
            Port = data.Proxy.Port,
            Username = data.Proxy.NeedsAuthentication ? data.Proxy.Username : null,
            Password = data.Proxy.NeedsAuthentication ? data.Proxy.Password : null
        };
    }

    private sealed record RealBrowserLaunchResponse
    {
        public string BrowserWSEndpoint { get; init; }
        public int? ProcessId { get; init; }
        public bool Success { get; init; }
        public string Error { get; init; }
    }

    private sealed record RealBrowserLaunchOptions
    {
        public string[] Args { get; init; } = Array.Empty<string>();
        public bool Headless { get; init; }
        public bool Turnstile { get; init; } = true;
        public bool DisableXvfb { get; init; } = true;
        public bool IgnoreAllFlags { get; init; }
        public RealBrowserConnectOptions ConnectOption { get; init; } = new();
        public RealBrowserCustomConfig CustomConfig { get; init; }
        public RealBrowserProxyOptions Proxy { get; init; }
    }

    private sealed record RealBrowserConnectOptions
    {
        public object DefaultViewport { get; init; }
        public bool IgnoreHTTPSErrors { get; init; }
    }

    private sealed record RealBrowserCustomConfig
    {
        public string ChromePath { get; init; }
    }

    private sealed record RealBrowserProxyOptions
    {
        public string Host { get; init; } = string.Empty;
        public int Port { get; init; }
        public string Username { get; init; }
        public string Password { get; init; }
    }
}
