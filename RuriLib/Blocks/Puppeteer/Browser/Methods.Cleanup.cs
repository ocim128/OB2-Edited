using PuppeteerSharp;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Yove.Proxy;

namespace RuriLib.Blocks.Puppeteer.Browser;

public static partial class Methods
{
    private static void CleanupBrowserSession(BotData data)
    {
        StopYoveProxyInternalServer(data);
        DisposeTrackedRealBrowserProcess(data);

        data.Objects.Remove("puppeteer");
        data.Objects.Remove("puppeteerPage");
        data.Objects.Remove("puppeteerFrame");
        data.Objects.Remove("puppeteer.pageList");
    }

    private static void StopYoveProxyInternalServer(BotData data)
    {
        if (data.TryGetObject<ProxyClient>("puppeteer.yoveproxy") is not { } proxyClient)
        {
            return;
        }

        proxyClient.Dispose();
        data.Objects.Remove("puppeteer.yoveproxy");
    }

    private static void DisposeTrackedRealBrowserProcess(BotData data)
    {
        if (data.TryGetObject<Process>("puppeteer.realBrowserProcess") is not { } storedProcess)
        {
            data.Objects.Remove("puppeteer.realBrowserProcess");
            data.Objects.Remove("puppeteer.realBrowserProcessId");
            return;
        }

        try
        {
            if (!storedProcess.HasExited)
            {
                storedProcess.Kill(true);
            }
        }
        catch
        {
        }
        finally
        {
            storedProcess.Dispose();
            data.Objects.Remove("puppeteer.realBrowserProcess");
            data.Objects.Remove("puppeteer.realBrowserProcessId");
        }
    }

    private static async Task InitializeBrowserSessionAsync(BotData data, IBrowser browser, bool applyDefaultHeaders,
        string defaultModeMessage)
    {
        var page = await GetOrCreatePrimaryPageAsync(browser).ConfigureAwait(false);

        data.SetObject("puppeteer", browser);
        SetPageAndFrame(data, page);

        await PreparePageAsync(data, page, applyDefaultHeaders, authenticateProxy: true, logStealthActivation: true,
            defaultModeMessage).ConfigureAwait(false);

        await InitializePageTracking(data, browser).ConfigureAwait(false);
    }

    private static async Task<IPage> GetOrCreatePrimaryPageAsync(IBrowser browser)
    {
        var existingPages = await browser.PagesAsync().ConfigureAwait(false);
        var page = existingPages.FirstOrDefault() ?? await browser.NewPageAsync().ConfigureAwait(false);

        foreach (var openPage in await browser.PagesAsync().ConfigureAwait(false))
        {
            if (openPage != page && openPage.Url == "about:blank")
            {
                await openPage.CloseAsync().ConfigureAwait(false);
            }
        }

        return page;
    }

    private static async Task OpenRealBrowserAsync(BotData data, string extraCmdLineArgs)
    {
        var browserArgs = BuildBrowserArguments(data, extraCmdLineArgs, includeDefaultArgs: true);
        data.Logger.Log($"Browser arguments: {string.Join(" ", browserArgs)}", LogColors.Yellow);

        var scriptsDirectory = ResolveScriptsDirectory();
        var launcherPath = Path.Combine(scriptsDirectory, "puppeteer-real-browser.js");
        if (!File.Exists(launcherPath))
        {
            throw new FileNotFoundException(
                $"Unable to find puppeteer-real-browser.js in '{scriptsDirectory}'. Make sure the npm dependencies in RuriLib/Scripts are installed.");
        }

        var realBrowserModulePath = Path.Combine(scriptsDirectory, "node_modules", "puppeteer-real-browser");
        if (!Directory.Exists(realBrowserModulePath))
        {
            throw new DirectoryNotFoundException(
                $"puppeteer-real-browser dependencies not found in '{scriptsDirectory}'. Run 'npm install' inside RuriLib/Scripts before using the real browser option.");
        }

        var chromePath = data.Providers.PuppeteerBrowser.ChromeBinaryLocation;
        RealBrowserCustomConfig customConfig = string.IsNullOrWhiteSpace(chromePath)
            ? null
            : new RealBrowserCustomConfig { ChromePath = chromePath };

        RealBrowserProxyOptions proxyOptions = null;
        if (data.UseProxy && data.Proxy != null)
        {
            proxyOptions = new RealBrowserProxyOptions
            {
                Host = data.Proxy.Host,
                Port = data.Proxy.Port,
                Username = data.Proxy.NeedsAuthentication ? data.Proxy.Username : null,
                Password = data.Proxy.NeedsAuthentication ? data.Proxy.Password : null
            };
        }

        var launchOptions = new RealBrowserLaunchOptions
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
            CustomConfig = customConfig,
            Proxy = proxyOptions
        };

        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(launchOptions, RealBrowserJsonOptions)));

        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = $"\"{launcherPath}\" \"{payload}\"",
            WorkingDirectory = scriptsDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Node.js process.");
        }
        catch (Exception ex)
        {
            throw new Exception(
                "Failed to start puppeteer-real-browser launcher. Ensure Node.js 16+ is installed and available on PATH.",
                ex);
        }

        using var cancellationRegistration = data.CancellationToken.Register(static state =>
        {
            if (state is Process proc && !proc.HasExited)
            {
                try
                {
                    proc.Kill(true);
                }
                catch
                {
                }
            }
        }, process);

        try
        {
            string handshakeLine;
            try
            {
                handshakeLine = await process.StandardOutput.ReadLineAsync().WaitAsync(data.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch
                    {
                    }
                }

                throw;
            }

            if (string.IsNullOrWhiteSpace(handshakeLine))
            {
                var details = await CollectProcessFailureDetailsAsync(process, data.CancellationToken).ConfigureAwait(false);
                throw new Exception($"puppeteer-real-browser did not return a browser WebSocket endpoint. {details}");
            }

            var launchResponse = JsonSerializer.Deserialize<RealBrowserLaunchResponse>(handshakeLine, RealBrowserJsonOptions);
            if (launchResponse is null || !launchResponse.Success || string.IsNullOrWhiteSpace(launchResponse.BrowserWSEndpoint))
            {
                var details = await CollectProcessFailureDetailsAsync(process, data.CancellationToken).ConfigureAwait(false);
                var reason = launchResponse?.Error ?? "unknown error";
                throw new Exception($"Failed to launch puppeteer-real-browser: {reason}. {details}");
            }

            var browser = await PuppeteerSharp.Puppeteer.ConnectAsync(new ConnectOptions
            {
                BrowserWSEndpoint = launchResponse.BrowserWSEndpoint,
                DefaultViewport = null
            }).ConfigureAwait(false);

            data.SetObject("puppeteer.realBrowserProcess", process, false);
            data.SetObject("puppeteer.realBrowserProcessId", launchResponse.ProcessId ?? process.Id, false);

            await InitializeBrowserSessionAsync(data, browser, applyDefaultHeaders: false,
                defaultModeMessage: "puppeteer-real-browser connected with default mode.").ConfigureAwait(false);

            data.Logger.Log("Connected to puppeteer-real-browser.", LogColors.Green);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
            }

            process.Dispose();
            data.Objects.Remove("puppeteer.realBrowserProcess");
            data.Objects.Remove("puppeteer.realBrowserProcessId");
            throw;
        }
    }

    private static async Task OpenPuppeteerSharpBrowser(BotData data, string extraCmdLineArgs = "")
    {
        var browserArgs = BuildBrowserArguments(data, extraCmdLineArgs, includeDefaultArgs: true);
        data.Logger.Log($"Browser arguments: {string.Join(" ", browserArgs)}", LogColors.Yellow);

        var browser = await PuppeteerSharp.Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = data.ConfigSettings.BrowserSettings.Headless,
            Args = browserArgs.ToArray(),
            AcceptInsecureCerts = data.ConfigSettings.BrowserSettings.IgnoreHttpsErrors,
            SlowMo = 0,
            Timeout = 30000,
            ExecutablePath = data.Providers.PuppeteerBrowser.ChromeBinaryLocation,
            DefaultViewport = null,
            IgnoredDefaultArgs =
            [
                "--enable-automation",
                "--enable-blink-features=IdleDetection",
                "--enable-blink-features=AutomationControlled",
                "--disable-extensions",
                "--disable-component-extensions-with-background-pages"
            ]
        }).ConfigureAwait(false);

        data.Logger.Log("Puppeteer browser launched successfully.", LogColors.Green);

        await InitializeBrowserSessionAsync(data, browser, applyDefaultHeaders: true,
            defaultModeMessage: "Default mode - standard browser behavior.").ConfigureAwait(false);
    }

    private static string ResolveScriptsDirectory()
    {
        static bool TryGetScriptsDirectory(string root, out string scriptsPath)
        {
            var direct = Path.Combine(root, "Scripts");
            if (Directory.Exists(direct) && File.Exists(Path.Combine(direct, "puppeteer-real-browser.js")))
            {
                scriptsPath = direct;
                return true;
            }

            var nested = Path.Combine(root, "RuriLib", "Scripts");
            if (Directory.Exists(nested) && File.Exists(Path.Combine(nested, "puppeteer-real-browser.js")))
            {
                scriptsPath = nested;
                return true;
            }

            scriptsPath = string.Empty;
            return false;
        }

        var baseCandidates = new List<string>
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(typeof(Methods).Assembly.Location),
            Directory.GetCurrentDirectory()
        };

        foreach (var candidate in baseCandidates
                     .Where(static c => !string.IsNullOrWhiteSpace(c))
                     .Select(static c => Path.GetFullPath(c))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryGetScriptsDirectory(candidate, out var resolved))
            {
                return resolved;
            }

            var current = candidate;
            for (var depth = 0; depth < 10 && !string.IsNullOrEmpty(current); depth++)
            {
                var parent = Directory.GetParent(current);
                if (parent is null)
                {
                    break;
                }

                if (TryGetScriptsDirectory(parent.FullName, out resolved))
                {
                    return resolved;
                }

                current = parent.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Unable to locate the Scripts directory. Verify that RuriLib/Scripts exists alongside the source and npm install has been executed there.");
    }

    private static async Task<string> CollectProcessFailureDetailsAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }
            }
        }

        var stderr = string.Empty;
        try
        {
            stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        var stdout = string.Empty;
        try
        {
            stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            builder.Append(stderr.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(stdout.Trim());
        }

        return builder.Length > 0
            ? $"Details: {builder}"
            : "Check the puppeteer-real-browser logs for more information.";
    }

    private static async Task InitializePageTracking(BotData data, IBrowser browser)
    {
        var pageList = new List<string>();
        var pages = await browser.PagesAsync().ConfigureAwait(false);

        foreach (var page in pages)
        {
            pageList.Add(page.Target.TargetId);
        }

        data.SetObject("puppeteer.pageList", pageList);

        browser.TargetCreated += (_, e) =>
        {
            if (e.Target.Type != TargetType.Page)
            {
                return;
            }

            lock (pageList)
            {
                if (!pageList.Contains(e.Target.TargetId))
                {
                    pageList.Add(e.Target.TargetId);
                }
            }
        };

        browser.TargetDestroyed += (_, e) =>
        {
            if (e.Target.Type != TargetType.Page)
            {
                return;
            }

            lock (pageList)
            {
                if (pageList.Contains(e.Target.TargetId))
                {
                    pageList.Remove(e.Target.TargetId);
                }
            }
        };
    }
}
