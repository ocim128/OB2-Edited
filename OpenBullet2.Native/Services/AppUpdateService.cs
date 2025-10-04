using Newtonsoft.Json;
using OpenBullet2.Native.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Media;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;


namespace OpenBullet2.Native.Services;

public interface IAppUpdateService
{
    Task CheckForUpdatesAsync();
}

public class AppUpdateService : IAppUpdateService
{
    private readonly Dispatcher dispatcher;
    public AppUpdateService()
    {
        dispatcher = Application.Current?.Dispatcher ?? throw new InvalidOperationException("WPF dispatcher is not available.");
    }



    public async Task CheckForUpdatesAsync()
    {
        if (!dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(async () =>
            {
                await RunUpdateFlowOnStaAsync().ConfigureAwait(true);
            }).Task.ConfigureAwait(true);
            return;
        }

        await RunUpdateFlowOnStaAsync().ConfigureAwait(true);
    }

    private async Task RunUpdateFlowOnStaAsync()
    {
        try
        {
            _ = Task.Run(async () =>
            {
                try { await CleanupOldUpdateFiles().ConfigureAwait(false); }
                catch (Exception bgEx) { Debug.WriteLine($"Cleanup task error: {bgEx.Message}"); }
            });

            Alert.Success("Update Check", "Checking for updates...");

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenBullet2-Native-Updater/1.0");

            const string latestUrl = "https://api.github.com/repos/ocim128/OB2-Edited/releases/latest";

            async Task<HttpResponseMessage> GetWithRetryAsync(string url, int attempts = 3)
            {
                var delay = 1000;
                for (var i = 1; i <= attempts; i++)
                {
                    try
                    {
                        var resp = await httpClient.GetAsync(url).ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode) return resp;
                        if ((int)resp.StatusCode is >= 500 and < 600)
                        {
                            await Task.Delay(delay).ConfigureAwait(false);
                            delay *= 2;
                            continue;
                        }
                        return resp;
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

            var response = await GetWithRetryAsync(latestUrl).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        "Failed to check for updates. Please check your internet connection or visit:\nhttps://github.com/ocim128/OB2-Edited/releases",
                        "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                return;
            }

            var jsonContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var releaseInfo = JsonConvert.DeserializeObject<dynamic>(jsonContent);

            var latestVersion = (string)releaseInfo.tag_name;
            var currentVersionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
            string currentVersion = "Unknown";

            if (File.Exists(currentVersionPath))
            {
                try
                {
                    currentVersion = (await File.ReadAllTextAsync(currentVersionPath).ConfigureAwait(false)).Trim();
                }
                catch (Exception ioEx)
                {
                    Debug.WriteLine($"Failed reading version.txt: {ioEx.Message}");
                }
            }

            if (!string.Equals(currentVersion, "Unknown", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(currentVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
            {
                dispatcher.Invoke(() =>
                {
                    Alert.Success("Update Check", "You are already running the latest version!");
                    PlayPopSound();
                });
                return;
            }

            await DownloadAndInstallUpdate(releaseInfo, latestVersion).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            dispatcher.Invoke(() =>
                Alert.Error("Update Error",
                    $"Failed to check for updates: {ex.Message}\n\nPlease visit manually:\nhttps://github.com/ocim128/OB2-Edited/releases"));
        }
    }

    private async Task DownloadAndInstallUpdate(dynamic releaseInfo, string latestVersion)
    {
        try
        {
            var assets = releaseInfo.assets;
            string downloadUrl = null;
            long fileSize = 0;

            foreach (var asset in assets)
            {
                string assetName = asset.name.ToString().ToLower();
                if (assetName.Contains("windows") || assetName.Contains("win") || assetName.EndsWith(".zip") || assetName.EndsWith(".rar") || assetName.Contains("ob2"))
                {
                    downloadUrl = asset.browser_download_url.ToString();
                    fileSize = asset.size != null ? (long)asset.size : 0;
                    break;
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                dispatcher.Invoke(() =>
                {
                    MessageBox.Show("No suitable download found for Windows. Opening release page...",
                        "Download Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                });

                var startInfo = new ProcessStartInfo
                {
                    FileName = releaseInfo.html_url.ToString(),
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"OpenBullet2Update_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(tempDir);

            var currentDir = AppDomain.CurrentDomain.BaseDirectory;
            var backupDir = Path.Combine(tempDir, "backup");
            Directory.CreateDirectory(backupDir);

            await CreateBackup(currentDir, backupDir).ConfigureAwait(false);

            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            var downloadPath = Path.Combine(tempDir, fileName);

            bool needsDownload = true;
            if (File.Exists(downloadPath))
            {
                try
                {
                    var existingFileInfo = new FileInfo(downloadPath);

                    long expectedSize = fileSize;
                    if (expectedSize == 0)
                    {
                        using var httpClient = new HttpClient();
                        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenBullet2-Native-Updater/1.0");
                        var headResponse = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, downloadUrl)).ConfigureAwait(false);
                        expectedSize = headResponse.Content.Headers.ContentLength ?? 0;
                    }

                    if (expectedSize > 0 && existingFileInfo.Length == expectedSize && await VerifyFileIntegrity(downloadPath).ConfigureAwait(false))
                    {
                        needsDownload = false;
                        MessageBox.Show("Update file already downloaded and verified. Proceeding with installation...",
                            "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        File.Delete(downloadPath);
                    }
                }
                catch
                {
                    try { File.Delete(downloadPath); } catch { }
                }
            }

            Window progressWindow = null!;
            ProgressBar progressBar = null!;
            Label statusLabel = null!;

            await dispatcher.InvokeAsync(() =>
            {
                progressWindow = new Window
                {
                    Title = "Downloading Update",
                    Width = 400,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    Topmost = true
                };

                progressBar = new ProgressBar
                {
                    Margin = new Thickness(20),
                    Height = 20
                };

                statusLabel = new Label
                {
                    Content = "Downloading...",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(20, 10, 20, 0)
                };

                var stackPanel = new StackPanel();
                stackPanel.Children.Add(statusLabel);
                stackPanel.Children.Add(progressBar);
                progressWindow.Content = stackPanel;

                progressWindow.Show();
            }).Task.ConfigureAwait(true);

            if (needsDownload)
            {
                int maxRetries = 3;
                int retryDelay = 2000;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        statusLabel.Dispatcher.Invoke(() => statusLabel.Content = attempt > 1 ? $"Downloading (Attempt {attempt}/{maxRetries})..." : "Downloading...");
                        await DownloadFileWithProgress(downloadUrl, downloadPath, progressBar, statusLabel).ConfigureAwait(false);
                        break;
                    }
                    catch (Exception ex) when (attempt < maxRetries)
                    {
                        statusLabel.Dispatcher.Invoke(() => statusLabel.Content = $"Download failed (Attempt {attempt}/{maxRetries}): {ex.Message}\nRetrying in {retryDelay / 1000} seconds...");
                        await Task.Delay(retryDelay).ConfigureAwait(false);
                        retryDelay *= 2;

                        try { File.Delete(downloadPath); } catch { }
                    }
                    catch (Exception ex) when (attempt == maxRetries)
                    {
                        throw new Exception($"Download failed after {maxRetries} attempts: {ex.Message}", ex);
                    }
                }
            }
            else
            {
                progressBar.Dispatcher.Invoke(() => progressBar.Value = 100);
                statusLabel.Dispatcher.Invoke(() => statusLabel.Content = "Using existing verified download...");
            }

            await dispatcher.InvokeAsync(() =>
            {
                statusLabel.Content = "Extracting...";
                progressBar.Value = 100;
            }).Task.ConfigureAwait(true);

            var extractPath = Path.Combine(tempDir, "extracted");

            if (Directory.Exists(extractPath))
            {
                try
                {
                    Directory.Delete(extractPath, true);
                }
                catch
                {
                    extractPath = Path.Combine(tempDir, $"extracted_{DateTime.Now.Ticks}");
                }
            }

            Directory.CreateDirectory(extractPath);

            var fileExtension = Path.GetExtension(downloadPath).ToLower();
            if (fileExtension == ".zip")
            {
                try
                {
                    ZipFile.ExtractToDirectory(downloadPath, extractPath);

                    if (!Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories).Any())
                    {
                        throw new InvalidOperationException("Extraction resulted in no files");
                    }

                    var extractedItems = Directory.GetDirectories(extractPath);
                    if (extractedItems.Length == 1 && !Directory.GetFiles(extractPath).Any())
                    {
                        extractPath = extractedItems[0];
                    }
                }
                catch (Exception ex)
                {
                    await dispatcher.InvokeAsync(() => progressWindow.Close()).Task.ConfigureAwait(true);
                    await dispatcher.InvokeAsync(() =>
                        MessageBox.Show($"Failed to extract update file: {ex.Message}\n\nPlease download and extract manually.",
                            "Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error)).Task.ConfigureAwait(true);
                    return;
                }
            }
            else if (fileExtension == ".rar")
            {
                await dispatcher.InvokeAsync(() => progressWindow.Close()).Task.ConfigureAwait(true);
                MessageBox.Show(
                    $"Downloaded update file: {fileName}\n\n" +
                    $"This is a RAR archive. Please extract it manually to:\n{currentDir}\n\n" +
                    $"The file has been saved to: {downloadPath}\n\n" +
                    $"After extraction, restart the application.",
                    "Manual Extraction Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Process.Start("explorer.exe", tempDir);
                return;
            }
            else
            {
                await dispatcher.InvokeAsync(() => progressWindow.Close()).Task.ConfigureAwait(true);
                await dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Unsupported file format: {fileExtension}", "Error", MessageBoxButton.OK, MessageBoxImage.Error)).Task.ConfigureAwait(true);
                return;
            }

            await dispatcher.InvokeAsync(() => progressWindow.Close()).Task.ConfigureAwait(true);

            var updateScript = Path.Combine(tempDir, "update.bat");
            var rollbackScript = Path.Combine(tempDir, "rollback.bat");
            var exePath = Process.GetCurrentProcess().MainModule.FileName;

            var versionFile = Path.Combine(currentDir, "version.txt");
            var logFile = Path.Combine(tempDir, "update.log");

            var scriptContent = "@echo off\n" +
                "setlocal enabledelayedexpansion\n" +
                $"set LOGFILE={logFile}\n" +
                "echo %date% %time% - Starting OpenBullet2 Update Installation >> %LOGFILE%\n" +
                "timeout /t 3 /nobreak > nul\n" +
                "taskkill /f /im OpenBullet2.Native.exe 2>nul\n" +
                "timeout /t 2 /nobreak > nul\n" +
                "echo Creating rollback script...\n" +
                $"echo @echo off > \"{rollbackScript}\"\n" +
                $"echo xcopy /E /Y /R \"{backupDir}\\*\" \"{currentDir}\" 2^>nul >> \"{rollbackScript}\"\n" +
                $"echo start \"\" \"{exePath}\" >> \"{rollbackScript}\"\n" +
                "set RETRY_COUNT=0\n" +
                ":retry_copy\n" +
                "set /a RETRY_COUNT+=1\n" +
                "echo %date% %time% - Copying files (attempt %RETRY_COUNT%) >> %LOGFILE%\n" +
                $"xcopy /E /Y /R \"{extractPath}\\*\" \"{currentDir}\" 2>>%LOGFILE%\n" +
                "if errorlevel 1 (\n" +
                "    if %RETRY_COUNT% LSS 5 (\n" +
                "        timeout /t 2 /nobreak > nul\n" +
                "        goto retry_copy\n" +
                "    ) else (\n" +
                "        echo %date% %time% - CRITICAL: File copy failed after 5 attempts >> %LOGFILE%\n" +
                $"        call \"{rollbackScript}\"\n" +
                "        exit /b 1\n" +
                "    )\n" +
                ")\n" +
                "set VERSION_RETRY=0\n" +
                ":retry_version\n" +
                "set /a VERSION_RETRY+=1\n" +
                $"echo {latestVersion} > \"{versionFile}\" 2>>%LOGFILE%\n" +
                "if errorlevel 1 (\n" +
                "    if %VERSION_RETRY% LSS 3 (\n" +
                "        timeout /t 1 /nobreak > nul\n" +
                "        goto retry_version\n" +
                "    )\n" +
                ")\n" +
                $"if not exist \"{Path.Combine(currentDir, "OpenBullet2.Native.exe")}\" (\n" +
                "    echo %date% %time% - CRITICAL: Main executable missing, running rollback >> %LOGFILE%\n" +
                $"    call \"{rollbackScript}\"\n" +
                "    exit /b 1\n" +
                ")\n" +
                "echo %date% %time% - Update completed successfully >> %LOGFILE%\n" +
                $"start \"\" \"{exePath}\"\n" +
                "timeout /t 5 /nobreak > nul\n" +
                $"rd /s /q \"{tempDir}\" 2>nul\n" +
                $"del \"{updateScript}\" 2>nul\n" +
                "exit";

            await File.WriteAllTextAsync(updateScript, scriptContent).ConfigureAwait(false);

            var updateProcess = new ProcessStartInfo
            {
                FileName = updateScript,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(updateProcess);

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update_error.log");
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Update Error: {ex}\n\n";
                File.AppendAllText(logPath, logEntry);
            }
            catch { }

            var msg = ex.Message ?? string.Empty;
            var isCrossThreadUiError =
                msg.Contains("different thread owns it", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("The calling thread cannot access this object", StringComparison.OrdinalIgnoreCase);

            if (!isCrossThreadUiError)
            {
                var errorMessage = $"Update failed: {ex.Message}\n\n";

                var backupDirs = Directory.GetDirectories(Path.GetTempPath(), "OpenBullet2Update_*")
                    .Where(d => Directory.Exists(Path.Combine(d, "backup")))
                    .OrderByDescending(d => Directory.GetCreationTime(d))
                    .ToArray();

                if (backupDirs.Any())
                {
                    var latestBackup = Path.Combine(backupDirs.First(), "backup");
                    errorMessage += $"A backup is available at: {latestBackup}\n";
                    errorMessage += "You can manually copy it back to restore the previous version.\n";
                }

                dispatcher.Invoke(() =>
                    MessageBox.Show(errorMessage,
                        "Update Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error));
            }
        }
    }

    private static async Task DownloadFileWithProgress(string url, string destination, ProgressBar progressBar, Label statusLabel)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            totalRead += read;

            if (totalBytes > 0)
            {
                var progress = (double)totalRead / totalBytes * 100;
                progressBar.Dispatcher.Invoke(() => progressBar.Value = progress);
                statusLabel.Dispatcher.Invoke(() => statusLabel.Content = $"Downloading... {progress:F1}%");
            }
            else
            {
                progressBar.Dispatcher.Invoke(() => progressBar.IsIndeterminate = true);
            }
        }

        progressBar.Dispatcher.Invoke(() => progressBar.Value = 100);
        statusLabel.Dispatcher.Invoke(() => statusLabel.Content = "Download complete, validating file...");

        if (!await VerifyFileIntegrity(destination).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Downloaded file failed integrity check");
        }
    }

    private static async Task CreateBackup(string sourceDir, string backupDir)
    {
        await Task.Run(() =>
        {
            void CopyDirectory(string sourcePath, string destPath)
            {
                var dir = new DirectoryInfo(sourcePath);

                if (!dir.Exists)
                {
                    return;
                }

                Directory.CreateDirectory(destPath);

                foreach (var file in dir.GetFiles())
                {
                    var targetFilePath = Path.Combine(destPath, file.Name);
                    file.CopyTo(targetFilePath, true);
                }

                foreach (var subDir in dir.GetDirectories())
                {
                    if (subDir.Name.Equals("UserData", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    CopyDirectory(subDir.FullName, Path.Combine(destPath, subDir.Name));
                }
            }

            CopyDirectory(sourceDir, backupDir);
        }).ConfigureAwait(false);
    }

    private static async Task CleanupOldUpdateFiles()
    {
        try
        {
            var tempDirectories = Directory.GetDirectories(Path.GetTempPath(), "OpenBullet2Update_*");

            foreach (var dir in tempDirectories)
            {
                var directoryInfo = new DirectoryInfo(dir);

                if (DateTime.Now - directoryInfo.CreationTime > TimeSpan.FromDays(7))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        await Task.Delay(50).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private static async Task<bool> VerifyFileIntegrity(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            var fileInfo = new FileInfo(filePath);

            if (fileInfo.Length == 0)
                return false;

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[Math.Min(8192, (int)Math.Min(fileInfo.Length, int.MaxValue))];
            var bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

            if (bytesRead == 0)
                return false;

            var extension = Path.GetExtension(filePath).ToLower();

            if (extension == ".zip")
            {
                try
                {
                    using var archive = ZipFile.OpenRead(filePath);
                    if (archive.Entries.Count == 0)
                        return false;

                    var firstEntry = archive.Entries.First();
                    using var entryStream = firstEntry.Open();
                    var testBuffer = new byte[Math.Min(1024, (int)Math.Max(1, firstEntry.Length))];
                    _ = await entryStream.ReadAsync(testBuffer, 0, testBuffer.Length).ConfigureAwait(false);

                    return true;
                }
                catch
                {
                    return false;
                }
            }

            if (extension == ".rar")
            {
                fileStream.Position = 0;
                var signature = new byte[7];
                await fileStream.ReadAsync(signature, 0, 7).ConfigureAwait(false);

                return signature.Length >= 4 &&
                       signature[0] == 0x52 && signature[1] == 0x61 &&
                       signature[2] == 0x72 && signature[3] == 0x21;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void PlayPopSound()
    {
        var soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-sound.mp3");

        if (!File.Exists(soundPath))
        {
            SystemSounds.Asterisk.Play();
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                dispatcher.Invoke(() =>
                {
                    var player = new System.Windows.Media.MediaPlayer();
                    player.Open(new Uri(soundPath));
                    player.Volume = 0.7;
                    player.Play();

                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    timer.Tick += (s, e) => { timer.Stop(); player.Close(); };
                    timer.Start();
                });
            }
            catch
            {
                SystemSounds.Asterisk.Play();
            }
        });
    }
}



