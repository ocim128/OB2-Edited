using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Flux.Native.ViewModels.Base;
using System.IO.Hashing;

namespace Flux.Native.ViewModels.Tools;

public sealed class LineReducerToolViewModel : ToolCardViewModelBase, IDisposable
{
    private static readonly UTF8Encoding Utf8NoBomEncoding = new(false);
    private readonly RelayCommand runCommand;
    private readonly RelayCommand cancelCommand;
    private readonly RelayCommand clearCompareFilesCommand;
    private readonly RelayCommand<LineReducerCompareFile> removeCompareFileCommand;

    private CancellationTokenSource? lineReducerCts;
    private string sourcePath = string.Empty;
    private string outputPath = string.Empty;
    private string compareSummary = "No comparison files selected.";
    private string progressText = "Idle.";
    private string statusMessage = string.Empty;
    private string statsText = "Awaiting first run.";
    private Brush statusBrush = Brushes.LightSteelBlue;
    private bool trimWhitespace = true;
    private bool ignoreCase;
    private bool isBusy;
    private bool hasStatus;
    private double progressValue;

    public LineReducerToolViewModel()
        : base("Line Reducer", "Text", "compare", "dedupe", "difference", "filter", "txt", "large files")
    {
        runCommand = new RelayCommand(() => _ = RunAsync(), () => !IsBusy);
        cancelCommand = new RelayCommand(Cancel, () => IsBusy);
        clearCompareFilesCommand = new RelayCommand(ClearCompareFiles, () => !IsBusy && CompareFiles.Count > 0);
        removeCompareFileCommand = new RelayCommand<LineReducerCompareFile>(RemoveCompareFile, file => !IsBusy && file is not null);
    }

    public ObservableCollection<LineReducerCompareFile> CompareFiles { get; } = new();

    public RelayCommand RunCommand => runCommand;

    public RelayCommand CancelCommand => cancelCommand;

    public RelayCommand ClearCompareFilesCommand => clearCompareFilesCommand;

    public RelayCommand<LineReducerCompareFile> RemoveCompareFileCommand => removeCompareFileCommand;

    public string SourcePath
    {
        get => sourcePath;
        set => SetProperty(ref sourcePath, value ?? string.Empty);
    }

    public string OutputPath
    {
        get => outputPath;
        set => SetProperty(ref outputPath, value ?? string.Empty);
    }

    public bool TrimWhitespace
    {
        get => trimWhitespace;
        set => SetProperty(ref trimWhitespace, value);
    }

    public bool IgnoreCase
    {
        get => ignoreCase;
        set => SetProperty(ref ignoreCase, value);
    }

    public string CompareSummary
    {
        get => compareSummary;
        private set => SetProperty(ref compareSummary, value);
    }

    public double ProgressValue
    {
        get => progressValue;
        private set => SetProperty(ref progressValue, value);
    }

    public string ProgressText
    {
        get => progressText;
        private set => SetProperty(ref progressText, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public Brush StatusBrush
    {
        get => statusBrush;
        private set => SetProperty(ref statusBrush, value);
    }

    public string StatsText
    {
        get => statsText;
        private set => SetProperty(ref statsText, value);
    }

    public bool HasStatus
    {
        get => hasStatus;
        private set
        {
            if (SetProperty(ref hasStatus, value))
            {
                OnPropertyChanged(nameof(StatusVisibility));
            }
        }
    }

    public Visibility StatusVisibility => HasStatus ? Visibility.Visible : Visibility.Collapsed;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                runCommand.RaiseCanExecuteChanged();
                cancelCommand.RaiseCanExecuteChanged();
                clearCompareFilesCommand.RaiseCanExecuteChanged();
                removeCompareFileCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CancelVisibility));
                OnPropertyChanged(nameof(AreInputsEnabled));
            }
        }
    }

    public Visibility CancelVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

    public bool AreInputsEnabled => !IsBusy;

    public void SetSourcePath(string path)
    {
        SourcePath = path ?? string.Empty;
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            OutputPath = SuggestOutputPath(SourcePath);
        }
    }

    public void SetOutputPath(string path)
    {
        OutputPath = path ?? string.Empty;
    }

    public void AddCompareFiles(IEnumerable<string> paths)
    {
        if (IsBusy)
        {
            SetStatus("Wait for the current run to finish before editing files.", Brushes.OrangeRed);
            return;
        }

        var added = 0;
        var skipped = 0;

        foreach (var fileName in paths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(fileName);
            }
            catch
            {
                skipped++;
                continue;
            }

            if (string.Equals(normalizedPath, SourcePath, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            if (CompareFiles.Any(existing => existing.FullPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }

            try
            {
                var info = new FileInfo(normalizedPath);
                if (!info.Exists)
                {
                    skipped++;
                    continue;
                }

                CompareFiles.Add(new LineReducerCompareFile(info.FullName, info.Length));
                added++;
            }
            catch
            {
                skipped++;
            }
        }

        UpdateCompareSummary();

        if (added > 0)
        {
            SetStatus($"Added {added} comparison file(s).", Brushes.LawnGreen);
        }
        else if (skipped > 0)
        {
            SetStatus("No new comparison files were added.", Brushes.OrangeRed);
        }
    }

    public void ClearCompareFiles()
    {
        if (IsBusy || CompareFiles.Count == 0)
        {
            return;
        }

        CompareFiles.Clear();
        UpdateCompareSummary();
        SetStatus("Cleared comparison list.", Brushes.OrangeRed);
    }

    public void RemoveCompareFile(LineReducerCompareFile? entry)
    {
        if (IsBusy || entry is null)
        {
            return;
        }

        CompareFiles.Remove(entry);
        UpdateCompareSummary();
    }

    public async Task RunAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var source = SourcePath.Trim();
        var output = OutputPath.Trim();
        var comparisonFiles = CompareFiles.Select(file => file.FullPath).ToList();

        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            SetStatus("Select an existing main file to continue.", Brushes.OrangeRed);
            return;
        }

        if (comparisonFiles.Count == 0)
        {
            SetStatus("Add at least one comparison file.", Brushes.OrangeRed);
            return;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            output = SuggestOutputPath(source);
            OutputPath = output;
        }

        string normalizedOutput;
        string normalizedSource;

        try
        {
            normalizedOutput = Path.GetFullPath(output);
            normalizedSource = Path.GetFullPath(source);
        }
        catch (Exception ex)
        {
            SetStatus($"Invalid path: {ex.Message}", Brushes.OrangeRed);
            return;
        }

        if (string.Equals(normalizedOutput, normalizedSource, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Output file must be different from the main file.", Brushes.OrangeRed);
            return;
        }

        if (comparisonFiles.Any(file => string.Equals(Path.GetFullPath(file), normalizedOutput, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("Output file cannot overwrite a comparison file.", Brushes.OrangeRed);
            return;
        }

        try
        {
            var outputDirectory = Path.GetDirectoryName(normalizedOutput);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Unable to create output directory: {ex.Message}", Brushes.OrangeRed);
            return;
        }

        IsBusy = true;
        ProgressValue = 0;
        ProgressText = "Preparing...";
        SetStatus("Indexing comparison files...", Brushes.LightSteelBlue);

        lineReducerCts?.Dispose();
        lineReducerCts = new CancellationTokenSource();
        var options = new LineReducerOptions(TrimWhitespace, IgnoreCase);

        try
        {
            var progress = new Progress<LineReductionProgress>(UpdateProgress);
            var result = await ExecuteAsync(source, comparisonFiles, normalizedOutput, options, progress, lineReducerCts.Token);

            SetStatus($"Completed. Removed {result.RemovedLines:N0} line(s).", Brushes.LawnGreen);
            StatsText =
                $"Indexed {result.IndexedLines:N0} comparison lines ({Flux.Native.Helpers.HumanReadable.Bytes(result.ComparisonBytes)})." +
                $"{Environment.NewLine}Processed {result.ProcessedSourceLines:N0} source lines ({Flux.Native.Helpers.HumanReadable.Bytes(result.SourceBytes)}): kept {result.WrittenLines:N0}, removed {result.RemovedLines:N0}." +
                $"{Environment.NewLine}Elapsed {result.Elapsed:mm\\:ss}. Output saved to {normalizedOutput}.";
        }
        catch (OperationCanceledException)
        {
            SetStatus("Operation cancelled.", Brushes.OrangeRed);
            TryDeleteFile(normalizedOutput);
        }
        catch (Exception ex)
        {
            SetStatus($"Line reduction failed: {ex.Message}", Brushes.OrangeRed);
            TryDeleteFile(normalizedOutput);
        }
        finally
        {
            ProgressValue = 0;
            ProgressText = "Idle.";
            lineReducerCts?.Dispose();
            lineReducerCts = null;
            IsBusy = false;
        }
    }

    public void Cancel()
    {
        if (!IsBusy)
        {
            return;
        }

        lineReducerCts?.Cancel();
    }

    public void Dispose()
    {
        lineReducerCts?.Cancel();
        lineReducerCts?.Dispose();
    }

    private void UpdateCompareSummary()
    {
        CompareSummary = CompareFiles.Count == 0
            ? "No comparison files selected."
            : $"{CompareFiles.Count} file(s) • {Flux.Native.Helpers.HumanReadable.Bytes(CompareFiles.Sum(file => file.Length))} total";

        clearCompareFilesCommand.RaiseCanExecuteChanged();
        removeCompareFileCommand.RaiseCanExecuteChanged();
    }

    private void SetStatus(string message, Brush brush)
    {
        StatusMessage = message;
        StatusBrush = brush;
        HasStatus = true;
    }

    private void UpdateProgress(LineReductionProgress progress)
    {
        ProgressValue = Math.Max(0, Math.Min(100, progress.Percent));

        var builder = new StringBuilder(progress.Stage);
        builder.Append($" | Removed {progress.RemovedLines:N0} line(s)");
        if (progress.ProcessedSourceLines > 0)
        {
            builder.Append($", processed {progress.ProcessedSourceLines:N0} line(s)");
        }

        ProgressText = builder.ToString();
    }

    private static string SuggestOutputPath(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "reduced.txt");
        }

        var directory = Path.GetDirectoryName(sourcePath);
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        var candidateName = $"{fileName}_reduced{extension}";
        return string.IsNullOrEmpty(directory) ? candidateName : Path.Combine(directory, candidateName);
    }

    private static async Task<LineReducerResult> ExecuteAsync(
        string sourcePath,
        IReadOnlyList<string> comparisonFiles,
        string outputPath,
        LineReducerOptions options,
        IProgress<LineReductionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var comparisonBytes = comparisonFiles.Sum(GetFileLengthSafe);
        var sourceBytes = GetFileLengthSafe(sourcePath);
        var totalBytes = Math.Max(1, comparisonBytes + sourceBytes);
        var signatures = new HashSet<LineFingerprint>(EstimateCapacity(comparisonBytes));
        long indexedLines = 0;
        long comparisonBytesCompleted = 0;

        using var fingerprintFactory = new LineFingerprintFactory();

        foreach (var comparison in comparisonFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = new FileStream(
                comparison,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1 << 20,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 20);

            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                signatures.Add(fingerprintFactory.Create(line, options.TrimWhitespace, options.IgnoreCase));
                indexedLines++;

                if (indexedLines % 25000 == 0)
                {
                    var percent = (comparisonBytesCompleted + stream.Position) / (double)totalBytes * 100d;
                    progress?.Report(new LineReductionProgress(Math.Min(98, percent), $"Indexing comparison files ({indexedLines:N0})", 0, 0, 0, indexedLines));
                }
            }

            comparisonBytesCompleted += stream.Position;
            var percentAfterFile = comparisonBytesCompleted / (double)totalBytes * 100d;
            progress?.Report(new LineReductionProgress(Math.Min(99, percentAfterFile), $"Indexed {indexedLines:N0} comparison lines", 0, 0, 0, indexedLines));
        }

        var newline = DetectSourceNewLine(sourcePath);
        long processedLines = 0;
        long removedLines = 0;
        long writtenLines = 0;

        await using var sourceStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1 << 20,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sourceReader = new StreamReader(sourceStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 20);
        _ = sourceReader.Peek();
        var writerEncoding = DetermineOutputEncoding(sourceReader);

        await using var outputStream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1 << 20,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var writer = new StreamWriter(outputStream, writerEncoding, bufferSize: 1 << 20, leaveOpen: false)
        {
            NewLine = newline
        };

        while (true)
        {
            var line = await sourceReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            processedLines++;

            var fingerprint = fingerprintFactory.Create(line, options.TrimWhitespace, options.IgnoreCase);
            if (signatures.Contains(fingerprint))
            {
                removedLines++;
            }
            else
            {
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                writtenLines++;
            }

            if (processedLines % 5000 == 0)
            {
                var percent = (comparisonBytes + sourceStream.Position) / (double)totalBytes * 100d;
                progress?.Report(new LineReductionProgress(Math.Min(100, percent), $"Processing source ({processedLines:N0})", processedLines, removedLines, writtenLines, indexedLines));
            }
        }

        await writer.FlushAsync().ConfigureAwait(false);
        stopwatch.Stop();

        progress?.Report(new LineReductionProgress(100, "Completed", processedLines, removedLines, writtenLines, indexedLines));

        return new LineReducerResult(processedLines, removedLines, writtenLines, indexedLines, sourceBytes, comparisonBytes, stopwatch.Elapsed);
    }

    private static int EstimateCapacity(long comparisonBytes)
    {
        const int minCapacity = 1024;
        const int maxCapacity = 2_000_000;

        if (comparisonBytes <= 0)
        {
            return minCapacity;
        }

        var estimated = comparisonBytes / 64;
        if (estimated < minCapacity)
        {
            return minCapacity;
        }

        if (estimated > maxCapacity)
        {
            return maxCapacity;
        }

        return (int)estimated;
    }

    private static long GetFileLengthSafe(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static string DetectSourceNewLine(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan);
            var previous = -1;

            while (true)
            {
                var current = stream.ReadByte();
                if (current == -1)
                {
                    return Environment.NewLine;
                }

                if (current == '\n')
                {
                    return previous == '\r' ? "\r\n" : "\n";
                }

                if (previous == '\r')
                {
                    return "\r";
                }

                previous = current;
            }
        }
        catch
        {
            return Environment.NewLine;
        }
    }

    private static Encoding DetermineOutputEncoding(StreamReader reader)
    {
        try
        {
            var encoding = reader.CurrentEncoding;
            if (encoding is UTF8Encoding)
            {
                return Utf8NoBomEncoding;
            }

            return encoding ?? Utf8NoBomEncoding;
        }
        catch
        {
            return Utf8NoBomEncoding;
        }
    }

    public sealed class LineReducerCompareFile
    {
        public LineReducerCompareFile(string fullPath, long length)
        {
            FullPath = fullPath;
            Length = length;
            DisplayName = Path.GetFileName(fullPath);
            Details = $"{Flux.Native.Helpers.HumanReadable.Bytes(length)} • {fullPath}";
        }

        public string FullPath { get; }

        public long Length { get; }

        public string DisplayName { get; }

        public string Details { get; }
    }

    private sealed record LineReducerResult(
        long ProcessedSourceLines,
        long RemovedLines,
        long WrittenLines,
        long IndexedLines,
        long SourceBytes,
        long ComparisonBytes,
        TimeSpan Elapsed);

    private sealed record LineReductionProgress(
        double Percent,
        string Stage,
        long ProcessedSourceLines,
        long RemovedLines,
        long WrittenLines,
        long IndexedLines);

    private readonly record struct LineReducerOptions(bool TrimWhitespace, bool IgnoreCase);

    private sealed class LineFingerprintFactory : IDisposable
    {
        private const int MaxReusableChars = 1 << 20;
        private const int MaxReusableBytes = 1 << 20;

        private char[]? charBuffer;
        private byte[]? byteBuffer;

        public LineFingerprint Create(string? line, bool trimWhitespace, bool ignoreCase)
        {
            ReadOnlySpan<char> span = line is null ? ReadOnlySpan<char>.Empty : line.AsSpan();

            if (trimWhitespace)
            {
                span = span.Trim();
            }

            if (span.Length == 0)
            {
                return default;
            }

            ReadOnlySpan<char> normalized = span;
            char[]? rentedChars = null;

            if (ignoreCase)
            {
                if (span.Length <= MaxReusableChars)
                {
                    EnsureCharBuffer(span.Length);
                    for (var i = 0; i < span.Length; i++)
                    {
                        charBuffer![i] = char.ToUpperInvariant(span[i]);
                    }

                    normalized = charBuffer.AsSpan(0, span.Length);
                }
                else
                {
                    rentedChars = ArrayPool<char>.Shared.Rent(span.Length);
                    for (var i = 0; i < span.Length; i++)
                    {
                        rentedChars[i] = char.ToUpperInvariant(span[i]);
                    }

                    normalized = rentedChars.AsSpan(0, span.Length);
                }
            }

            var maxByteCount = Utf8NoBomEncoding.GetMaxByteCount(normalized.Length);
            Span<byte> buffer;
            byte[]? rentedBytes = null;

            if (maxByteCount <= MaxReusableBytes)
            {
                EnsureByteBuffer(maxByteCount);
                buffer = byteBuffer.AsSpan(0, maxByteCount);
            }
            else
            {
                rentedBytes = ArrayPool<byte>.Shared.Rent(maxByteCount);
                buffer = rentedBytes.AsSpan(0, maxByteCount);
            }

            var bytesWritten = Utf8NoBomEncoding.GetBytes(normalized, buffer);
            var hashedSpan = buffer.Slice(0, bytesWritten);
            var fingerprint = new LineFingerprint(XxHash3.HashToUInt64(hashedSpan), XxHash64.HashToUInt64(hashedSpan), bytesWritten);

            if (rentedBytes is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedBytes);
            }

            if (rentedChars is not null)
            {
                ArrayPool<char>.Shared.Return(rentedChars);
            }

            return fingerprint;
        }

        private void EnsureCharBuffer(int length)
        {
            if (charBuffer is not null && charBuffer.Length >= length)
            {
                return;
            }

            if (charBuffer is not null)
            {
                ArrayPool<char>.Shared.Return(charBuffer);
            }

            charBuffer = ArrayPool<char>.Shared.Rent(length);
        }

        private void EnsureByteBuffer(int length)
        {
            if (byteBuffer is not null && byteBuffer.Length >= length)
            {
                return;
            }

            if (byteBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(byteBuffer);
            }

            byteBuffer = ArrayPool<byte>.Shared.Rent(length);
        }

        public void Dispose()
        {
            if (charBuffer is not null)
            {
                ArrayPool<char>.Shared.Return(charBuffer);
            }

            if (byteBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(byteBuffer);
            }
        }
    }

    private readonly struct LineFingerprint : IEquatable<LineFingerprint>
    {
        public LineFingerprint(ulong primary, ulong secondary, int byteLength)
        {
            Primary = primary;
            Secondary = secondary;
            ByteLength = byteLength;
        }

        public ulong Primary { get; }

        public ulong Secondary { get; }

        public int ByteLength { get; }

        public bool Equals(LineFingerprint other)
            => Primary == other.Primary && Secondary == other.Secondary && ByteLength == other.ByteLength;

        public override bool Equals(object? obj)
            => obj is LineFingerprint other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Primary, Secondary, ByteLength);
    }
}
