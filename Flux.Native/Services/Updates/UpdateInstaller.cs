using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Flux.Native.Services;

internal sealed class UpdateInstaller
{
    private readonly Dispatcher dispatcher;

    public UpdateInstaller(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
    }

    public async Task CreateBackupAsync(string sourceDir, string backupDir)
    {
        await Task.Run(() => CopyDirectory(sourceDir, backupDir)).ConfigureAwait(false);
    }

    public async Task CleanupOldUpdateFilesAsync()
    {
        try
        {
            var tempDirectories = Directory.GetDirectories(Path.GetTempPath(), "FluxUpdate_*");

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
                        // Ignore cleanup errors.
                    }
                }
            }
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }

    public async Task<string?> ExtractUpdatePackageAsync(string downloadPath, string tempDir)
    {
        var extractPath = PrepareExtractDirectory(tempDir);
        var fileExtension = Path.GetExtension(downloadPath).ToLowerInvariant();

        if (fileExtension == ".zip")
        {
            return await ExtractZipAsync(downloadPath, extractPath).ConfigureAwait(false);
        }

        if (fileExtension == ".rar")
        {
            await ShowManualRarExtractionMessageAsync(downloadPath, tempDir).ConfigureAwait(false);
            return null;
        }

        await dispatcher.InvokeAsync(() =>
            MessageBox.Show($"Unsupported file format: {fileExtension}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        return null;
    }

    public async Task CreateAndRunUpdateBatchAsync(string tempDir, string currentDir, string backupDir, string extractPath, string latestVersion)
    {
        var updateScript = Path.Combine(tempDir, "update.bat");
        var rollbackScript = Path.Combine(tempDir, "rollback.bat");
        var exePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Could not determine the current executable path.");
        var versionFile = Path.Combine(currentDir, "version.txt");
        var logFile = Path.Combine(tempDir, "update.log");

        await File.WriteAllTextAsync(updateScript, BuildUpdateScriptContent(
            updateScript, rollbackScript, logFile, backupDir, currentDir, extractPath, latestVersion, versionFile, exePath, tempDir)).ConfigureAwait(false);

        Process.Start(new ProcessStartInfo
        {
            FileName = updateScript,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        Application.Current.Shutdown();
    }

    private static void CopyDirectory(string sourcePath, string destPath)
    {
        var directory = new DirectoryInfo(sourcePath);
        if (!directory.Exists)
        {
            return;
        }

        Directory.CreateDirectory(destPath);

        foreach (var file in directory.GetFiles())
        {
            file.CopyTo(Path.Combine(destPath, file.Name), true);
        }

        foreach (var subDirectory in directory.GetDirectories())
        {
            if (subDirectory.Name.Equals("UserData", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyDirectory(subDirectory.FullName, Path.Combine(destPath, subDirectory.Name));
        }
    }

    private static string PrepareExtractDirectory(string tempDir)
    {
        var extractPath = Path.Combine(tempDir, "extracted");
        if (Directory.Exists(extractPath))
        {
            try
            {
                Directory.Delete(extractPath, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateInstaller] Failed to delete existing extract directory '{extractPath}': {ex.Message}");
                extractPath = Path.Combine(tempDir, $"extracted_{DateTime.Now.Ticks}");
            }
        }

        Directory.CreateDirectory(extractPath);
        return extractPath;
    }

    private async Task<string?> ExtractZipAsync(string downloadPath, string extractPath)
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

            return extractPath;
        }
        catch (Exception ex)
        {
            await dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to extract update file: {ex.Message}\n\nPlease download and extract manually.",
                    "Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error));
            return null;
        }
    }

    private async Task ShowManualRarExtractionMessageAsync(string downloadPath, string tempDir)
    {
        var fileName = Path.GetFileName(downloadPath);

        await dispatcher.InvokeAsync(() =>
            MessageBox.Show(
                $"Downloaded update file: {fileName}\n\n" +
                $"This is a RAR archive. Please extract it manually to:\n{AppDomain.CurrentDomain.BaseDirectory}\n\n" +
                $"The file has been saved to: {downloadPath}\n\n" +
                $"After extraction, restart the application.",
                "Manual Extraction Required", MessageBoxButton.OK, MessageBoxImage.Information));

        Process.Start("explorer.exe", tempDir);
    }

    private static string BuildUpdateScriptContent(string updateScript, string rollbackScript, string logFile, string backupDir,
        string currentDir, string extractPath, string latestVersion, string versionFile, string exePath, string tempDir)
    {
        return "@echo off\n" +
            "setlocal enabledelayedexpansion\n" +
            $"set LOGFILE={logFile}\n" +
            "echo %date% %time% - Starting Flux Update Installation >> %LOGFILE%\n" +
            "timeout /t 3 /nobreak > nul\n" +
            "taskkill /f /im Flux.Native.exe 2>nul\n" +
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
            $"if not exist \"{Path.Combine(currentDir, "Flux.Native.exe")}\" (\n" +
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
    }
}
