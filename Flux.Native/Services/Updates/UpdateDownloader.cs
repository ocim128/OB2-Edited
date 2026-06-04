using Flux.Native.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Flux.Native.Services;

internal sealed class UpdateDownloader
{
    private const string UserAgent = "Flux-Native-Updater/1.0";

    private readonly ILogger<UpdateDownloader> logger;

    public UpdateDownloader(ILogger<UpdateDownloader> logger)
    {
        this.logger = logger;
    }

    public UpdateAssetSelection FindSuitableAsset(UpdateAsset[] assets)
    {
        foreach (var asset in assets)
        {
            var assetName = asset.Name.ToLowerInvariant();
            if (assetName.Contains("windows") || assetName.Contains("win") || assetName.EndsWith(".zip") || assetName.EndsWith(".rar") || assetName.Contains("ob2"))
            {
                return new UpdateAssetSelection(asset.BrowserDownloadUrl, asset.Size);
            }
        }

        throw new InvalidOperationException("Download loop exited unexpectedly.");
    }

    public async Task<bool> DownloadUpdateAsync(string url, string destination, IUpdateProgress progressUi, long expectedSize, CancellationToken cancellationToken)
    {
        if (await TryUseExistingDownloadAsync(url, destination, expectedSize).ConfigureAwait(false))
        {
            progressUi.Report(100, "Using existing verified download...");
            return true;
        }

        var maxRetries = 3;
        var retryDelay = 2000;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await DownloadFileWithProgressAsync(url, destination, progressUi, expectedSize, cancellationToken).ConfigureAwait(false);
                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                progressUi.Report(0, $"Download failed (Attempt {attempt}/{maxRetries}): {ex.Message}\nRetrying in {retryDelay / 1000} seconds...");
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                retryDelay *= 2;

                try
                {
                    File.Delete(destination);
                }
                catch
                {
                    // Ignore failed cleanup between retries.
                }
            }
            catch (Exception ex) when (attempt == maxRetries)
            {
                throw new InvalidOperationException($"Download failed after {maxRetries} attempts: {ex.Message}", ex);
            }
        }

        return false;
    }

    private async Task<bool> TryUseExistingDownloadAsync(string url, string destination, long expectedSize)
    {
        if (!File.Exists(destination))
        {
            return false;
        }

        try
        {
            var existingFileInfo = new FileInfo(destination);
            var resolvedExpectedSize = await ResolveExpectedSizeAsync(url, expectedSize).ConfigureAwait(false);

            if (resolvedExpectedSize > 0
                && existingFileInfo.Length == resolvedExpectedSize
                && await VerifyFileIntegrityAsync(destination, resolvedExpectedSize, msg => AppendUpdateLog($"Existing download check: {msg}")).ConfigureAwait(false))
            {
                return true;
            }

            File.Delete(destination);
        }
        catch
        {
            try
            {
                File.Delete(destination);
            }
            catch
            {
                // Ignore failed cleanup.
            }
        }

        return false;
    }

    private static async Task<long> ResolveExpectedSizeAsync(string url, long expectedSize)
    {
        if (expectedSize > 0)
        {
            return expectedSize;
        }

        var httpClient = DownloadClient.Value;
        using var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url)).ConfigureAwait(false);
        return response.Content.Headers.ContentLength ?? 0;
    }

    private static readonly Lazy<HttpClient> DownloadClient = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    });

    private async Task DownloadFileWithProgressAsync(string url, string destination, IUpdateProgress progressUi, long expectedSize, CancellationToken cancellationToken)
    {
        var httpClient = DownloadClient.Value;
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        await using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

        var buffer = new byte[81920];
        long totalRead = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastUiUpdate = TimeSpan.Zero;

        await using (var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, buffer.Length, useAsync: true))
        {
            int read;
            while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalRead += read;

                var elapsed = stopwatch.Elapsed;
                if (elapsed - lastUiUpdate >= TimeSpan.FromMilliseconds(250))
                {
                    lastUiUpdate = elapsed;
                    ReportDownloadProgress(progressUi, totalRead, totalBytes, elapsed);
                }
            }

            await fileStream.FlushAsync().ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        stopwatch.Stop();

        var finalSpeed = stopwatch.Elapsed.TotalSeconds > 0 ? totalRead / stopwatch.Elapsed.TotalSeconds : 0;
        var finalSpeedText = finalSpeed > 0 ? $"{HumanReadable.Bytes(finalSpeed)}/s" : "0 B/s";
        var finalDownloadText = HumanReadable.Bytes(totalRead);
        var durationText = FormatDuration(stopwatch.Elapsed);

        progressUi.Report(100, $"Download complete ({finalDownloadText} in {durationText}, avg {finalSpeedText}) - validating file...");

        var resolvedExpectedSize = totalBytes > 0 ? totalBytes : expectedSize;
        if (!await VerifyFileIntegrityAsync(destination, resolvedExpectedSize, msg => AppendUpdateLog($"Post-download check: {msg}")).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Downloaded file failed integrity check. See update_error.log for details.");
        }
    }

    private static void ReportDownloadProgress(IUpdateProgress progressUi, long totalRead, long totalBytes, TimeSpan elapsed)
    {
        var speed = elapsed.TotalSeconds > 0 ? totalRead / elapsed.TotalSeconds : 0;
        var downloadedText = HumanReadable.Bytes(totalRead);
        var speedText = speed > 0 ? $"{HumanReadable.Bytes(speed)}/s" : "0 B/s";

        if (totalBytes > 0)
        {
            var percent = (double)totalRead / totalBytes * 100;
            var eta = speed > 0 ? TimeSpan.FromSeconds(Math.Max(0, (totalBytes - totalRead) / speed)) : TimeSpan.Zero;
            var etaText = speed > 0 ? $" ETA {FormatDuration(eta)}" : string.Empty;
            var totalText = HumanReadable.Bytes(totalBytes);

            progressUi.Report(percent, $"Downloading... {percent:F1}% ({downloadedText} / {totalText}, {speedText}{etaText})");
            return;
        }

        progressUi.SetIndeterminate(true);
        progressUi.Report(0, $"Downloading... {downloadedText} ({speedText})");
    }

    private static async Task<bool> VerifyFileIntegrityAsync(string filePath, long expectedSize = 0, Action<string>? logFailure = null)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                logFailure?.Invoke($"Integrity check failed for '{filePath}': file does not exist.");
                return false;
            }

            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            if (fileStream.Length == 0)
            {
                logFailure?.Invoke($"Integrity check failed for '{filePath}': file size is zero.");
                return false;
            }

            if (expectedSize > 0 && fileStream.Length != expectedSize)
            {
                logFailure?.Invoke($"Integrity check failed for '{filePath}': size mismatch (expected {expectedSize}, actual {fileStream.Length}).");
                return false;
            }

            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            if (extension == ".zip")
            {
                var signature = new byte[4];
                var read = await fileStream.ReadAsync(signature.AsMemory(0, signature.Length)).ConfigureAwait(false);

                if (read < 4 || signature[0] != 0x50 || signature[1] != 0x4B)
                {
                    logFailure?.Invoke($"Integrity check failed for '{filePath}': invalid ZIP header (bytes: {string.Join(" ", signature.Take(read))}).");
                    return false;
                }

                try
                {
                    fileStream.Position = 0;
                    using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: true);
                    if (!archive.Entries.Any(e => !string.IsNullOrEmpty(e.Name)))
                    {
                        logFailure?.Invoke($"Integrity check failed for '{filePath}': ZIP archive contains no files.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    logFailure?.Invoke($"Integrity check failed for '{filePath}': unable to read ZIP archive ({ex.Message}).");
                    return false;
                }

                return true;
            }

            if (extension == ".rar")
            {
                fileStream.Position = 0;
                var signature = new byte[7];
                var read = await fileStream.ReadAsync(signature.AsMemory(0, signature.Length)).ConfigureAwait(false);

                if (read >= 4 && signature[0] == 0x52 && signature[1] == 0x61 && signature[2] == 0x72 && signature[3] == 0x21)
                {
                    return true;
                }

                logFailure?.Invoke($"Integrity check failed for '{filePath}': invalid RAR header (bytes: {string.Join(" ", signature.Take(read))}).");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logFailure?.Invoke($"Integrity check failed for '{filePath}': exception during validation ({ex.Message}).");
            return false;
        }
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
        }

        return $"{span.Minutes:D2}:{span.Seconds:D2}";
    }

    private void AppendUpdateLog(string message)
    {
        logger.LogWarning("{Message}", message);
    }
}
