using Flux.Native.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Flux.Native.Services;

public interface IAppUpdateService
{
    Task CheckForUpdatesAsync();
}

internal class AppUpdateService : IAppUpdateService
{
    private readonly Dispatcher dispatcher;
    private readonly UpdateVersionChecker versionChecker;
    private readonly UpdateDownloader downloader;
    private readonly UpdateInstaller installer;
    private readonly ILogger<AppUpdateService> logger;

    public AppUpdateService(UpdateDownloader downloader, ILogger<AppUpdateService> logger)
    {
        dispatcher = Application.Current?.Dispatcher ?? throw new InvalidOperationException("WPF dispatcher is not available.");
        versionChecker = new UpdateVersionChecker();
        this.downloader = downloader;
        installer = new UpdateInstaller(dispatcher);
        this.logger = logger;
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
                try
                {
                    await installer.CleanupOldUpdateFilesAsync().ConfigureAwait(false);
                }
                catch (Exception bgEx)
                {
                    Debug.WriteLine($"Cleanup task error: {bgEx.Message}");
                }
            });

            Alert.Success("Update Check", "Checking for updates...");

            var availableUpdate = await versionChecker.CheckForUpdateAsync().ConfigureAwait(false);
            if (availableUpdate is null)
            {
                Alert.Success("Update Check", "You are running the latest version.");
                return;
            }

            var result = dispatcher.Invoke(() => MessageBox.Show(
                $"A new version ({availableUpdate.LatestVersion}) is available. You are currently on {availableUpdate.CurrentVersion}. Do you want to download and install it now?",
                "Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information));

            if (result == MessageBoxResult.Yes)
            {
                await DownloadAndInstallUpdateAsync(availableUpdate).ConfigureAwait(false);
            }
            else
            {
                Alert.Info("Update Check", "Update deferred.");
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"An error occurred during update check: {ex.Message}",
                "Update Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task DownloadAndInstallUpdateAsync(AvailableUpdateInfo availableUpdate)
    {
        try
        {
            var asset = downloader.FindSuitableAsset(availableUpdate.ReleaseInfo.Assets);
            if (string.IsNullOrEmpty(asset.DownloadUrl))
            {
                ShowMessage("No suitable download found for Windows. Opening release page...",
                    "Download Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Process.Start(new ProcessStartInfo
                {
                    FileName = availableUpdate.ReleaseInfo.HtmlUrl,
                    UseShellExecute = true
                });
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"FluxUpdate_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(tempDir);

            var currentDir = AppDomain.CurrentDomain.BaseDirectory;
            var backupDir = Path.Combine(tempDir, "backup");
            Directory.CreateDirectory(backupDir);

            await installer.CreateBackupAsync(currentDir, backupDir).ConfigureAwait(false);

            var fileName = Path.GetFileName(new Uri(asset.DownloadUrl).LocalPath);
            var downloadPath = Path.Combine(tempDir, fileName);

            using var cts = new CancellationTokenSource();
            var downloadCancelled = false;

            IUpdateProgress progressUi = null!;
            await dispatcher.InvokeAsync(() =>
            {
                progressUi = new WpfUpdateProgress(() =>
                {
                    downloadCancelled = true;
                    if (!cts.IsCancellationRequested)
                    {
                        cts.Cancel();
                    }
                });
                progressUi.Show();
            });

            var usedExistingDownload = false;

            try
            {
                usedExistingDownload = await downloader.DownloadUpdateAsync(
                    asset.DownloadUrl,
                    downloadPath,
                    progressUi,
                    asset.FileSize,
                    cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                downloadCancelled = true;
            }

            if (downloadCancelled || cts.IsCancellationRequested)
            {
                CleanupCancelledDownload(downloadPath);
                progressUi.Close();

                ShowMessage("Update download cancelled.",
                    "Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (usedExistingDownload)
            {
                ShowMessage("Update file already downloaded and verified. Proceeding with installation...",
                    "Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            progressUi.Report(100, "Extracting...");

            var extractPath = await installer.ExtractUpdatePackageAsync(downloadPath, tempDir).ConfigureAwait(false);
            if (extractPath is null)
            {
                progressUi.Close();
                return;
            }

            progressUi.Close();

            await installer.CreateAndRunUpdateBatchAsync(tempDir, currentDir, backupDir, extractPath, availableUpdate.LatestVersion).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TryAppendUpdateErrorLog(ex);

            var message = BuildUpdateErrorMessage(ex.Message);
            ShowMessage(message, "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowMessage(string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
    {
        dispatcher.Invoke(() => MessageBox.Show(message, title, buttons, icon));
    }

    private static void CleanupCancelledDownload(string downloadPath)
    {
        try
        {
            if (File.Exists(downloadPath))
            {
                File.Delete(downloadPath);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private void TryAppendUpdateErrorLog(Exception ex)
    {
        logger.LogError(ex, "Update Error");
    }

    private static string BuildUpdateErrorMessage(string errorMessage)
    {
        var message = $"Update failed: {errorMessage}\n\n";

        var backupDirs = Directory.GetDirectories(Path.GetTempPath(), "FluxUpdate_*")
            .Where(d => Directory.Exists(Path.Combine(d, "backup")))
            .OrderByDescending(d => Directory.GetCreationTime(d))
            .ToArray();

        if (backupDirs.Any())
        {
            var latestBackup = Path.Combine(backupDirs.First(), "backup");
            message += $"A backup is available at: {latestBackup}\n";
            message += "You can manually copy it back to restore the previous version.\n";
        }

        return message;
    }
}
