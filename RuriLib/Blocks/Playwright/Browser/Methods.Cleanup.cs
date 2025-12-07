using Microsoft.Playwright;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Playwright.Browser
{
    public static partial class Methods
    {

        private static void RegisterCleanupState(BotData data, IBrowser? browser, HashSet<string> tempSnapshotBeforeLaunch)
        {
            var cleanupState = new PlaywrightCleanupState(data);
            cleanupState.Register(browser, tempSnapshotBeforeLaunch);
            data.SetObject(PlaywrightCleanupStateKey, cleanupState);
        }

        private static void PerformCleanup(BotData data)
        {
            var cleanupState = data.TryGetObject<PlaywrightCleanupState>(PlaywrightCleanupStateKey);
            cleanupState?.StopManualCloseWatcher();

            var playwrightInstance = data.TryGetObject<IPlaywright>("playwrightInstance");
            if (playwrightInstance != null)
            {
                try
                {
                    playwrightInstance.Dispose();
                }
                catch (Exception ex)
                {
                    data.Logger.Log($"Failed to dispose Playwright instance: {ex.Message}", LogColors.Orange);
                }
            }

            var realBrowserProcessIdObj = data.TryGetObject<object>("playwright.realBrowserProcessId");
            if (realBrowserProcessIdObj is int realBrowserProcessId)
            {
                try
                {
                    var process = System.Diagnostics.Process.GetProcessById(realBrowserProcessId);
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch
                {
                    // Ignore if process is already terminated or inaccessible
                }

                data.SetObject("playwright.realBrowserProcessId", null);
            }

            // ALWAYS kill Playwright Firefox processes on cleanup (even if Close Browser wasn't called)
            // This handles cases where bot stops/errors without explicit browser close
            KillPlaywrightFirefoxProcesses(data);

            DeleteDirectoryIfExists(data, "playwright.tempFirefoxProfile", "temporary Firefox profile");
            DeleteDirectoryIfExists(data, "playwright.tempChromiumUserData", "temporary Chromium user data");
            DeleteTrackedArtifacts(data);

            data.Objects.Remove("playwright");
            data.Objects.Remove("playwrightContext");
            data.Objects.Remove("playwrightPage");
            data.Objects.Remove("playwrightInstance");
            data.Objects.Remove("playwright.tempFirefoxProfile");
            data.Objects.Remove("playwright.tempChromiumUserData");
            data.Objects.Remove("playwright.tempArtifacts");
            data.Objects.Remove("playwright.firefoxProcessIds");
            data.Objects.Remove(PlaywrightCleanupStateKey);
        }

        private static void DeleteDirectoryIfExists(BotData data, string key, string description)
        {
            var directoryPath = data.TryGetObject<string>(key);
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            DeleteFileSystemEntryIfExists(data, directoryPath, description);
        }

        private static void DeleteTrackedArtifacts(BotData data)
        {
            var artifacts = data.TryGetObject<IEnumerable<string>>("playwright.tempArtifacts");
            if (artifacts == null)
            {
                return;
            }

            foreach (var artifactPath in artifacts)
            {
                DeleteFileSystemEntryIfExists(data, artifactPath, "Playwright temporary artifact");
            }
        }

        private static void DeleteFileSystemEntryIfExists(BotData data, string path, string description)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                    data.Logger.Log($"Cleaned up {description}: {path}", LogColors.Yellow);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                    data.Logger.Log($"Cleaned up {description}: {path}", LogColors.Yellow);
                }
            }
            catch (Exception ex)
            {
                data.Logger.Log($"Failed to delete {description} ({path}): {ex.Message}", LogColors.Orange);
            }
        }

        private static void StorePlaywrightTempArtifacts(BotData data, HashSet<string> baseline)
        {
            try
            {
                var currentEntries = CapturePlaywrightTempEntries();
                if (baseline != null && baseline.Count > 0)
                {
                    currentEntries.ExceptWith(baseline);
                }

                if (currentEntries.Count > 0)
                {
                    data.SetObject("playwright.tempArtifacts", currentEntries.ToArray());
                }
                else
                {
                    data.Objects.Remove("playwright.tempArtifacts");
                }
            }
            catch (Exception ex)
            {
                data.Logger.Log($"Failed to track Playwright temporary directories: {ex.Message}", LogColors.Orange);
            }
        }

        private static HashSet<string> CapturePlaywrightTempEntries()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var tempDirectory = Path.GetTempPath();
                foreach (var entry in Directory.EnumerateDirectories(tempDirectory))
                {
                    var name = Path.GetFileName(entry);
                    if (!string.IsNullOrEmpty(name) && IsPlaywrightTempName(name))
                    {
                        result.Add(entry);
                    }
                }
            }
            catch
            {
                // Failing to enumerate temp entries should not block browser launch
            }

            return result;
        }

        private static bool IsPlaywrightTempName(string name)
        {
            return name.StartsWith("playwright", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("pw-", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("ms-playwright", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly string[] FirefoxProcessNames =
        {
            "firefox",
            "nightly",
            "librewolf",
            "camoufox",
            "waterfox"
        };

        private static Dictionary<int, string> CaptureFirefoxProcessSnapshot()
        {
            var snapshot = new Dictionary<int, string>();
            try
            {
                foreach (var process in Process.GetProcesses())
                {
                    if (!IsFirefoxProcessName(process.ProcessName))
                    {
                        continue;
                    }

                    snapshot[process.Id] = SafeGetProcessPath(process);
                }
            }
            catch
            {
                // Swallow - failing to capture snapshot should not block launch
            }

            return snapshot;
        }

        private static void StoreFirefoxProcessDelta(BotData data, Dictionary<int, string> baseline)
        {
            if (baseline == null)
            {
                data.Objects.Remove("playwright.firefoxProcessIds");
                return;
            }

            try
            {
                var current = CaptureFirefoxProcessSnapshot();
                var delta = current.Keys.Except(baseline.Keys).ToArray();
                if (delta.Length > 0)
                {
                    data.SetObject("playwright.firefoxProcessIds", delta, false);
                }
                else
                {
                    data.Objects.Remove("playwright.firefoxProcessIds");
                }
            }
            catch
            {
                data.Objects.Remove("playwright.firefoxProcessIds");
            }
        }

        private static bool IsFirefoxProcessName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            foreach (var alias in FirefoxProcessNames)
            {
                if (name.Equals(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string SafeGetProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private sealed class PlaywrightCleanupState
        {
            private readonly BotData _data;
            private IBrowser? _browser;
            private EventHandler<IBrowser>? _browserDisconnectedHandler;
            private int _cleanupTriggered;
            private HashSet<string> _tempSnapshotBeforeLaunch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private CancellationTokenSource? _manualCloseWatcherCts;

            public PlaywrightCleanupState(BotData data)
            {
                _data = data;
            }

            public void Register(IBrowser? browser, HashSet<string> tempSnapshotBeforeLaunch)
            {
                _tempSnapshotBeforeLaunch = tempSnapshotBeforeLaunch != null ? new HashSet<string>(tempSnapshotBeforeLaunch, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _browser = browser;

                if (_browser != null)
                {
                    _browserDisconnectedHandler = (_, _) =>
                    {
                        Cleanup("Playwright browser disconnected unexpectedly. Cleaning up temporary resources.");
                    };
                    _browser.Disconnected += _browserDisconnectedHandler;
                }

                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            }

            public void SuppressBrowserDisconnect()
            {
                if (_browser != null && _browserDisconnectedHandler != null)
                {
                    _browser.Disconnected -= _browserDisconnectedHandler;
                    _browserDisconnectedHandler = null;
                }
            }

            public bool Cleanup(string? logMessage)
            {
                if (Interlocked.Exchange(ref _cleanupTriggered, 1) == 1)
                {
                    return false;
                }

                SuppressBrowserDisconnect();
                StopManualCloseWatcher();
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

                if (!string.IsNullOrWhiteSpace(logMessage))
                {
                    _data.Logger.Log(logMessage!, LogColors.Yellow);
                }

                StorePlaywrightTempArtifacts(_data, _tempSnapshotBeforeLaunch);
                PerformCleanup(_data);
                return true;
            }

            private void OnProcessExit(object? sender, EventArgs e)
            {
                Cleanup(null);
            }

            public void StartManualCloseWatcher(bool enabled)
            {
                StopManualCloseWatcher();

                if (!enabled)
                {
                    return;
                }

                var tracked = _data.TryGetObject<int[]>("playwright.firefoxProcessIds");
                if (tracked == null || tracked.Length == 0)
                {
                    return;
                }

                _manualCloseWatcherCts = new CancellationTokenSource();
                _ = Task.Run(() => MonitorFirefoxManualCloseAsync(_data, tracked, _manualCloseWatcherCts.Token), _manualCloseWatcherCts.Token);
            }

            public void StopManualCloseWatcher()
            {
                if (_manualCloseWatcherCts != null)
                {
                    try
                    {
                        _manualCloseWatcherCts.Cancel();
                    }
                    catch
                    {
                    }
                    finally
                    {
                        _manualCloseWatcherCts.Dispose();
                        _manualCloseWatcherCts = null;
                    }
                }
            }
        }

        /// <summary>
        /// Kills all Firefox processes and deletes Playwright temp profiles.
        /// Aggressive cleanup to ensure no zombie processes or temp files remain.
        /// </summary>
        private static void KillPlaywrightFirefoxProcesses(BotData data)
        {
            var cleanupState = data.TryGetObject<PlaywrightCleanupState>(PlaywrightCleanupStateKey);
            cleanupState?.StopManualCloseWatcher();
            data.Logger.Log("Attempting Firefox cleanup...", LogColors.Yellow);

            try
            {
                var killedPids = KillTrackedFirefoxProcesses(data);

                // Step 2: Delete all Playwright temp profiles from %TEMP%
                try
                {
                    var tempPath = Path.GetTempPath();
                    var patterns = new[] { "playwright-*", "playwright-firefox-*", "playwright_*", "tmp*playwright*" };
                    var deletedCount = 0;
                    
                    foreach (var pattern in patterns)
                    {
                        var dirs = Directory.GetDirectories(tempPath, pattern, SearchOption.TopDirectoryOnly);
                        foreach (var dir in dirs)
                        {
                            try
                            {
                                Directory.Delete(dir, true);
                                deletedCount++;
                            }
                            catch
                            {
                                // In use or permission denied
                            }
                        }
                    }

                    if (deletedCount > 0)
                    {
                        data.Logger.Log($"Deleted {deletedCount} temp profile folder(s)", LogColors.Yellow);
                    }
                }
                catch (Exception ex)
                {
                    data.Logger.Log($"Temp cleanup error: {ex.Message}", LogColors.Orange);
                }

                if (killedPids.Count > 0)
                {
                    data.Logger.Log($"Γ£à Killed {killedPids.Count} Playwright Firefox process(es)", LogColors.Green);
                }
                else
                {
                    data.Logger.Log("No Playwright Firefox processes to kill", LogColors.Yellow);
                }
            }
            catch (Exception ex)
            {
                data.Logger.Log($"Cleanup failed: {ex.Message}", LogColors.Red);
            }
        }

        private static HashSet<int> KillTrackedFirefoxProcesses(BotData data)
        {
            var killed = new HashSet<int>();
            var tracked = data.TryGetObject<int[]>("playwright.firefoxProcessIds");
            if (tracked == null || tracked.Length == 0)
            {
                return killed;
            }

            foreach (var pid in tracked)
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (proc.HasExited)
                    {
                        continue;
                    }

                    proc.Kill(true);
                    killed.Add(pid);
                    data.Logger.Log($"  Killed tracked Firefox PID {pid}", LogColors.Yellow);
                }
                catch (Exception ex)
                {
                    data.Logger.Log($"  Failed to kill tracked PID {pid}: {ex.Message}", LogColors.Orange);
                }
            }

            data.Objects.Remove("playwright.firefoxProcessIds");
            return killed;
        }

        private static async Task MonitorFirefoxManualCloseAsync(BotData data, int[] trackedPids, CancellationToken token)
        {
            var logger = data.Logger;
            var seenWindows = new HashSet<int>();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var anyRunning = false;
                    var manualCloseDetected = false;

                    foreach (var pid in trackedPids)
                    {
                        Process proc;
                        try
                        {
                            proc = Process.GetProcessById(pid);
                        }
                        catch
                        {
                            continue;
                        }

                        if (proc.HasExited)
                        {
                            continue;
                        }

                        anyRunning = true;

                        var handle = SafeGetMainWindowHandle(proc);
                        if (HasVisibleWindow(handle))
                        {
                            seenWindows.Add(pid);
                            continue;
                        }

                        if (seenWindows.Contains(pid))
                        {
                            manualCloseDetected = true;
                            break;
                        }
                    }

                    if (manualCloseDetected)
                    {
                        logger.Log("Detected manual Firefox window close. Cleaning up Playwright resources...", LogColors.Yellow);
                        var cleanupState = data.TryGetObject<PlaywrightCleanupState>(PlaywrightCleanupStateKey);
                        if (cleanupState != null)
                        {
                            cleanupState.Cleanup("Firefox window closed manually. Cleaning up.");
                        }
                        else
                        {
                            KillPlaywrightFirefoxProcesses(data);
                        }
                        return;
                    }

                    if (!anyRunning)
                    {
                        break;
                    }

                    await Task.Delay(ManualClosePollInterval, token);
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.Log($"Manual close watcher error: {ex.Message}", LogColors.Orange);
            }
        }

        private static IntPtr SafeGetMainWindowHandle(Process process)
        {
            try
            {
                return process.MainWindowHandle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static bool HasVisibleWindow(IntPtr handle)
        {
            return handle != IntPtr.Zero && IsWindow(handle) && IsWindowVisible(handle);
        }

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
    }
}

