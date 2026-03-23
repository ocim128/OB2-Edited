using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Flux.Core.Repositories;
using Flux.Core.Services;
using Flux.Native.ViewModels.Base;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic.Devices;
using RuriLib;
using RuriLib.Helpers;
using RuriLib.Helpers.Transpilers;
using RuriLib.Logging;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Custom;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs;
using RuriLib.Models.Data;
using RuriLib.Models.Data.DataPools;
using RuriLib.Models.Environment;
using RuriLib.Services;

namespace Flux.Native.ViewModels.Tools;

public sealed class PerformanceBenchmarkToolViewModel : ToolCardViewModelBase, IDisposable
{
    private readonly ComputerInfo computerInfo = new();
    private readonly RelayCommand clearStatsCommand;
    private readonly RelayCommand runBenchmarkCommand;
    private readonly StringBuilder outputBuilder = new();

    private DispatcherTimer? benchmarkUpdateTimer;
    private Stopwatch? benchmarkStopwatch;
    private DateTime benchmarkStartTime;
    private bool benchmarkInitialized;
    private bool performanceMonitoringStarted;
    private bool isRunning;
    private bool hasStatus;
    private string outputLog = string.Empty;
    private string statusMessage = string.Empty;
    private string memoryUsageText = "0.0%";
    private string memoryUsageValue = "N/A";
    private string cpuUsageText = "LOW";
    private string cpuUsageValue = "0.0%";
    private string systemStatusText = "GOOD";
    private string systemStatusValue = "Optimal";
    private Brush statusBrush = Brushes.LightGreen;
    private Brush memoryUsageBrush = Brushes.LightGreen;
    private Brush cpuUsageBrush = Brushes.LightGreen;
    private Brush systemStatusBrush = Brushes.LightGreen;

    public PerformanceBenchmarkToolViewModel()
        : base("Performance Benchmark", "Performance", "metrics", "cpu", "memory", "system", "monitoring", "speed")
    {
        clearStatsCommand = new RelayCommand(ClearStats);
        runBenchmarkCommand = new RelayCommand(() => _ = RunAsync(), () => !IsRunning);
        InitializeDisplay();
    }

    public RelayCommand ClearStatsCommand => clearStatsCommand;

    public RelayCommand RunBenchmarkCommand => runBenchmarkCommand;

    public string OutputLog
    {
        get => outputLog;
        private set => SetProperty(ref outputLog, value);
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

    public bool IsRunning
    {
        get => isRunning;
        private set
        {
            if (SetProperty(ref isRunning, value))
            {
                runBenchmarkCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string MemoryUsageText
    {
        get => memoryUsageText;
        private set => SetProperty(ref memoryUsageText, value);
    }

    public string MemoryUsageValue
    {
        get => memoryUsageValue;
        private set => SetProperty(ref memoryUsageValue, value);
    }

    public Brush MemoryUsageBrush
    {
        get => memoryUsageBrush;
        private set => SetProperty(ref memoryUsageBrush, value);
    }

    public string CpuUsageText
    {
        get => cpuUsageText;
        private set => SetProperty(ref cpuUsageText, value);
    }

    public string CpuUsageValue
    {
        get => cpuUsageValue;
        private set => SetProperty(ref cpuUsageValue, value);
    }

    public Brush CpuUsageBrush
    {
        get => cpuUsageBrush;
        private set => SetProperty(ref cpuUsageBrush, value);
    }

    public string SystemStatusText
    {
        get => systemStatusText;
        private set => SetProperty(ref systemStatusText, value);
    }

    public string SystemStatusValue
    {
        get => systemStatusValue;
        private set => SetProperty(ref systemStatusValue, value);
    }

    public Brush SystemStatusBrush
    {
        get => systemStatusBrush;
        private set => SetProperty(ref systemStatusBrush, value);
    }

    public void ClearStats()
    {
        outputBuilder.Clear();
        OutputLog = string.Empty;
        HasStatus = false;
        StatusMessage = string.Empty;
        AppendLog("Performance statistics cleared.");
    }

    public async Task RunAsync()
    {
        IsRunning = true;

        try
        {
            if (!performanceMonitoringStarted)
            {
                LazyInitializePerformanceMonitoring();
            }

            benchmarkStartTime = DateTime.Now;
            benchmarkStopwatch = Stopwatch.StartNew();

            SetStatus("Running software performance benchmark...", Brushes.LightBlue);
            AppendLog($"Benchmark started at {benchmarkStartTime:HH:mm:ss}");

            var context = BuildBenchmarkContext();
            var steps = new List<Func<BenchmarkContext, Task<BenchmarkResult>>>
            {
                BenchmarkConfigReloadAsync,
                BenchmarkConfigSerializationAsync,
                BenchmarkStringBlockAsync,
                BenchmarkLoliCodeParsingAsync,
                BenchmarkWordlistDataPoolAsync,
                BenchmarkPluginDiscoveryAsync
            };

            var results = new List<BenchmarkResult>();
            foreach (var step in steps)
            {
                var result = await step(context);
                results.Add(result);
                AppendResultLog(result);
            }

            benchmarkStopwatch.Stop();

            var passed = results.Count(result => result.Success);
            var skipped = results.Count(result => result.Skipped);
            var failed = results.Count - passed - skipped;
            var totalDuration = results.Aggregate(TimeSpan.Zero, (current, result) => current + result.Duration);

            AppendLog("=== BENCHMARK COMPLETE ===");
            AppendLog($"Tests passed: {passed}, failed: {failed}, skipped: {skipped}");
            AppendLog($"Aggregate runtime: {totalDuration.TotalMilliseconds:F0}ms (wall clock {benchmarkStopwatch.ElapsedMilliseconds}ms)");
            AppendLog($"Benchmark completed at {DateTime.Now:HH:mm:ss}");

            if (failed > 0)
            {
                SetStatus("Software benchmark completed with errors", Brushes.OrangeRed);
            }
            else if (passed == 0)
            {
                SetStatus("Software benchmark could not run", Brushes.Orange);
            }
            else if (skipped > 0)
            {
                SetStatus("Software benchmark completed with partial coverage", Brushes.Gold);
            }
            else
            {
                SetStatus("Software benchmark completed successfully", Brushes.LightGreen);
            }
        }
        catch (Exception ex)
        {
            benchmarkStopwatch?.Stop();
            AppendLog($"Benchmark aborted: {ex.Message}");
            SetStatus($"Benchmark failed: {ex.Message}", Brushes.Red);
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void Dispose()
    {
        if (benchmarkUpdateTimer is not null)
        {
            benchmarkUpdateTimer.Stop();
            benchmarkUpdateTimer = null;
        }
    }

    private void LazyInitializePerformanceMonitoring()
    {
        if (benchmarkInitialized)
        {
            return;
        }

        benchmarkInitialized = true;
        performanceMonitoringStarted = true;

        benchmarkUpdateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        benchmarkUpdateTimer.Tick += (_, _) => UpdatePerformanceStats();
        benchmarkUpdateTimer.Start();
    }

    private void UpdatePerformanceStats()
    {
        if (!performanceMonitoringStarted)
        {
            return;
        }

        try
        {
            var memoryInfo = GetLightweightMemoryUsage();
            if (memoryInfo.usedMB != null)
            {
                MemoryUsageValue = $"{memoryInfo.usedMB} MB / {memoryInfo.totalMB} MB";
                MemoryUsageText = memoryInfo.percentage.ToString("F1") + "%";
                MemoryUsageBrush = GetPerformanceColor(memoryInfo.percentage);

                var cpuUsage = GetCpuUsage();
                CpuUsageValue = cpuUsage.ToString("F1") + "%";
                CpuUsageText = cpuUsage > 80 ? "HIGH" : cpuUsage > 50 ? "MED" : "LOW";
                CpuUsageBrush = GetPerformanceColor(cpuUsage);
            }

            var systemStatus = GetSystemStatus();
            if (!string.IsNullOrEmpty(systemStatus))
            {
                SystemStatusValue = systemStatus;
                SystemStatusText = systemStatus == "Optimal" ? "GOOD" : systemStatus == "Moderate" ? "WARN" : "POOR";
                SystemStatusBrush = systemStatus == "Optimal" ? Brushes.LightGreen : systemStatus == "Moderate" ? Brushes.Orange : Brushes.Red;
            }
        }
        catch
        {
        }
    }

    private (string? usedMB, string totalMB, double percentage) GetLightweightMemoryUsage()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64;
            var totalMemory = computerInfo.TotalPhysicalMemory;
            var percentage = totalMemory > 0 ? (double)workingSet / totalMemory * 100.0 : 0;

            return (
                usedMB: (workingSet / 1024 / 1024).ToString(),
                totalMB: (totalMemory / 1024 / 1024).ToString(),
                percentage);
        }
        catch
        {
            return (null, "N/A", 0);
        }
    }

    private BenchmarkContext BuildBenchmarkContext()
    {
        var context = new BenchmarkContext();

        try
        {
            context.ConfigService = App.ServiceProvider.GetService<ConfigService>();
        }
        catch (Exception ex)
        {
            AppendLog($"Config service unavailable: {ex.Message}");
        }

        try
        {
            context.SettingsService = App.ServiceProvider.GetService<RuriLibSettingsService>();
        }
        catch (Exception ex)
        {
            AppendLog($"Settings service unavailable: {ex.Message}");
        }

        if (context.SettingsService == null)
        {
            try
            {
                context.SettingsService = new RuriLibSettingsService(GetBenchmarkSettingsPath());
            }
            catch (Exception ex)
            {
                AppendLog($"Fallback settings initialization failed: {ex.Message}");
            }
        }

        try
        {
            context.PluginRepository = App.ServiceProvider.GetService<PluginRepository>();
        }
        catch (Exception ex)
        {
            AppendLog($"Plugin repository unavailable: {ex.Message}");
        }

        context.BotData = CreateBenchmarkBotData(context.SettingsService);

        return context;
    }

    private static string GetBenchmarkSettingsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "ob2-benchmark", "settings");
        Directory.CreateDirectory(path);
        return path;
    }

    private BotData? CreateBenchmarkBotData(RuriLibSettingsService? settingsService)
    {
        try
        {
            var effectiveSettings = settingsService ?? new RuriLibSettingsService(GetBenchmarkSettingsPath());
            var providers = new Providers(effectiveSettings);
            var logger = new BotLogger { Enabled = false };
            var wordlistType = effectiveSettings.Environment?.WordlistTypes?.FirstOrDefault()
                ?? new WordlistType
                {
                    Name = "Benchmark",
                    Regex = ".*",
                    Verify = false,
                    Separator = ":",
                    Slices = new[] { "DATA", "EXTRA" },
                    SlicesAlias = Array.Empty<string>()
                };

            var dataLine = new DataLine("benchmark:data", wordlistType);
            return new BotData(providers, new ConfigSettings(), logger, dataLine);
        }
        catch (Exception ex)
        {
            AppendLog($"Bot context initialization failed: {ex.Message}");
            return null;
        }
    }

    private void AppendResultLog(BenchmarkResult result)
    {
        if (result.Skipped)
        {
            AppendLog($"[Skipped] {result.Name}: {result.Details}");
        }
        else if (!result.Success)
        {
            var message = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Unknown error" : result.ErrorMessage;
            AppendLog($"[Failed] {result.Name} ({result.Duration.TotalMilliseconds:F0}ms): {message}");
        }
        else
        {
            AppendLog($"[Passed] {result.Name} ({result.Duration.TotalMilliseconds:F0}ms): {result.Details}");
        }
    }

    private async Task<BenchmarkResult> BenchmarkConfigReloadAsync(BenchmarkContext context)
    {
        const string name = "Config cache refresh";
        if (context.ConfigService == null)
        {
            return BenchmarkResult.SkippedResult(name, "Config service unavailable");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await context.ConfigService.ReloadConfigsAsync();
            var configCount = context.ConfigService.Configs?.Count() ?? 0;
            return BenchmarkResult.SuccessResult(name, stopwatch.Elapsed, $"{configCount} config(s) cached");
        }
        catch (Exception ex)
        {
            return BenchmarkResult.Failure(name, stopwatch.Elapsed, ex.Message);
        }
    }

    private async Task<BenchmarkResult> BenchmarkConfigSerializationAsync(BenchmarkContext _)
    {
        const string name = "Config serialization";
        var config = BuildSampleBenchmarkConfig();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var packed = await ConfigPacker.PackAsync(config);
            await using var stream = new MemoryStream(packed);
            var unpacked = await ConfigPacker.UnpackAsync(stream);
            var sizeKb = packed.Length / 1024d;
            return BenchmarkResult.SuccessResult(name, stopwatch.Elapsed, $"Packed {unpacked.Metadata?.Name ?? "config"} ({sizeKb:F1} KB)");
        }
        catch (Exception ex)
        {
            return BenchmarkResult.Failure(name, stopwatch.Elapsed, ex.Message);
        }
    }

    private Task<BenchmarkResult> BenchmarkStringBlockAsync(BenchmarkContext context)
    {
        const string name = "String block throughput";
        if (context.BotData == null)
        {
            return Task.FromResult(BenchmarkResult.SkippedResult(name, "Bot context unavailable"));
        }

        var samples = new[]
        {
            "The quick brown fox jumps over the lazy dog.",
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit.",
            "Flux diagnostics benchmark string payload.",
            "RuriLib string functions under load."
        };

        var replacements = new[] { "a", "e", "i", "o", "u" };
        const int iterations = 100_000;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var lengthAccumulator = 0;
            for (var i = 0; i < iterations; i++)
            {
                var input = samples[i % samples.Length];
                var upper = RuriLib.Blocks.Functions.String.Methods.ToUppercase(context.BotData, input);
                var reversed = RuriLib.Blocks.Functions.String.Methods.Reverse(context.BotData, upper);
                var sliceLength = Math.Min(16, reversed.Length);
                var sliced = sliceLength > 0
                    ? RuriLib.Blocks.Functions.String.Methods.Substring(context.BotData, reversed, 0, sliceLength)
                    : string.Empty;
                var replaced = RuriLib.Blocks.Functions.String.Methods.Replace(
                    context.BotData,
                    sliced,
                    replacements[i % replacements.Length],
                    replacements[(i + 1) % replacements.Length]);
                var random = RuriLib.Blocks.Functions.String.Methods.RandomString(context.BotData, "?l?u?d?l?u?d");
                lengthAccumulator += (replaced?.Length ?? 0) + (random?.Length ?? 0);
            }

            var opsPerSecond = iterations / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
            return Task.FromResult(BenchmarkResult.SuccessResult(name, stopwatch.Elapsed, $"{iterations:N0} string block invocations (~{opsPerSecond:N0} ops/s, aggregate output {lengthAccumulator:N0} chars)"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(BenchmarkResult.Failure(name, stopwatch.Elapsed, ex.Message));
        }
    }

    private Config BuildSampleBenchmarkConfig()
    {
        return new Config
        {
            Id = $"benchmark-{Guid.NewGuid():N}",
            Mode = ConfigMode.LoliCode,
            Metadata = new RuriLib.Models.Configs.ConfigMetadata
            {
                Name = "Benchmark Sample Config",
                Category = "Diagnostics",
                Author = "Flux"
            },
            Settings = new ConfigSettings(),
            Readme = "Synthetic config generated for diagnostics.",
            LoliCodeScript = "LOG \"Benchmark\"",
            StartupLoliCodeScript = "LOG \"Benchmark startup\""
        };
    }

    private static string BuildBenchmarkLoliScript()
    {
        var ids = Globals.DescriptorsRepository.Descriptors
            .Where(pair => pair.Value is AutoBlockDescriptor autoDescriptor && autoDescriptor.Parameters.Count == 0)
            .Select(pair => pair.Key)
            .Distinct()
            .Take(12)
            .ToList();

        if (!ids.Any())
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var id in ids)
        {
            builder.AppendLine($"BLOCK:{id}");
            builder.AppendLine("ENDBLOCK");
        }

        return builder.ToString();
    }

    private Task<BenchmarkResult> BenchmarkLoliCodeParsingAsync(BenchmarkContext _)
    {
        const string name = "LoliCode transpiler";
        var script = BuildBenchmarkLoliScript();

        if (string.IsNullOrWhiteSpace(script))
        {
            return Task.FromResult(BenchmarkResult.SkippedResult(name, "No parameterless blocks available for testing"));
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var stack = Loli2StackTranspiler.Transpile(script);
            return Task.FromResult(BenchmarkResult.SuccessResult(name, stopwatch.Elapsed, $"Transpiled {stack.Count} block(s)"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(BenchmarkResult.Failure(name, stopwatch.Elapsed, ex.Message));
        }
    }

    private Task<BenchmarkResult> BenchmarkWordlistDataPoolAsync(BenchmarkContext context)
    {
        const string name = "Wordlist ingestion";
        var wordlistType = context.SettingsService?.Environment?.WordlistTypes?.FirstOrDefault();

        if (wordlistType == null)
        {
            return Task.FromResult(BenchmarkResult.SkippedResult(name, "No wordlist types configured"));
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"ob2-wordlist-{Guid.NewGuid():N}.txt");
        var entries = GenerateBenchmarkWordlistEntries(2000);

        try
        {
            File.WriteAllLines(tempFile, entries);

            var wordlist = new Wordlist("Benchmark Wordlist", tempFile, wordlistType, "Diagnostics", countLines: false)
            {
                Total = entries.Length
            };

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var dataPool = new WordlistDataPool(wordlist);
                var enumerated = dataPool.DataList.Take(Math.Min(entries.Length, 1500)).Count();
                return Task.FromResult(BenchmarkResult.SuccessResult(name, stopwatch.Elapsed, $"Enumerated {enumerated} entry(ies) from disk"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(BenchmarkResult.Failure(name, stopwatch.Elapsed, ex.Message));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(BenchmarkResult.Failure(name, TimeSpan.Zero, ex.Message));
        }
        finally
        {
            try
            {
                File.Delete(tempFile);
            }
            catch
            {
            }
        }
    }

    private static string[] GenerateBenchmarkWordlistEntries(int count)
    {
        var lines = new string[count];
        for (var i = 0; i < count; i++)
        {
            lines[i] = $"user{i:0000}:password{i:0000}";
        }

        return lines;
    }

    private Task<BenchmarkResult> BenchmarkPluginDiscoveryAsync(BenchmarkContext context)
    {
        const string name = "Plugin catalogue scan";
        if (context.PluginRepository == null)
        {
            return Task.FromResult(BenchmarkResult.SkippedResult(name, "Plugin repository unavailable"));
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var pluginNames = context.PluginRepository.GetPluginNames().ToList();
            var preview = pluginNames.Count == 0
                ? "No plugins detected"
                : $"Loaded {pluginNames.Count} plugin(s) ({string.Join(", ", pluginNames.Take(3))}{(pluginNames.Count > 3 ? ", ..." : string.Empty)})";
            return Task.FromResult(BenchmarkResult.SuccessResult(name, stopwatch.Elapsed, preview));
        }
        catch (Exception ex)
        {
            return Task.FromResult(BenchmarkResult.Failure(name, stopwatch.Elapsed, ex.Message));
        }
    }

    private double GetCpuUsage()
    {
        try
        {
            var startTime = DateTime.UtcNow;
            var startCpu = Process.GetCurrentProcess().TotalProcessorTime;
            System.Threading.Thread.Sleep(1000);
            var endTime = DateTime.UtcNow;
            var endCpu = Process.GetCurrentProcess().TotalProcessorTime;
            var cpuUsedMs = (endCpu - startCpu).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
            return Math.Clamp(cpuUsageTotal * 100.0, 0.0, 100.0);
        }
        catch
        {
            return 0;
        }
    }

    private string GetSystemStatus()
    {
        try
        {
            var (_, _, memoryPercent) = GetSystemMemoryInfo();
            var cpuUsage = GetCpuUsage();

            if (memoryPercent < 50 && cpuUsage < 50)
            {
                return "Optimal";
            }

            if (memoryPercent < 75 && cpuUsage < 75)
            {
                return "Moderate";
            }

            return "High Load";
        }
        catch
        {
            return "Unknown";
        }
    }

    private (long totalMemory, long availableMemory, float usagePercent) GetSystemMemoryInfo()
    {
        try
        {
            var info = new ComputerInfo();
            var totalMemory = (long)info.TotalPhysicalMemory;
            var availableMemory = (long)info.AvailablePhysicalMemory;
            var usagePercent = totalMemory == 0 ? 0f : (float)(100.0 * (totalMemory - availableMemory) / totalMemory);
            return (totalMemory, availableMemory, usagePercent);
        }
        catch
        {
            return (0, 0, 0f);
        }
    }

    private static Brush GetPerformanceColor(double value)
    {
        if (value < 50)
        {
            return Brushes.LightGreen;
        }

        if (value < 80)
        {
            return Brushes.Orange;
        }

        return Brushes.Red;
    }

    private void AppendLog(string message)
    {
        outputBuilder.Append('[');
        outputBuilder.Append(DateTime.Now.ToString("HH:mm:ss"));
        outputBuilder.Append("] ");
        outputBuilder.AppendLine(message);
        OutputLog = outputBuilder.ToString();
    }

    private void SetStatus(string message, Brush brush)
    {
        StatusMessage = message;
        StatusBrush = brush;
        HasStatus = true;
    }

    private void InitializeDisplay()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64;
            var totalMemory = computerInfo.TotalPhysicalMemory;

            if (totalMemory > 0)
            {
                MemoryUsageValue = $"{workingSet / 1024 / 1024} MB / {totalMemory / 1024 / 1024} MB";
                MemoryUsageText = "0.0%";
                MemoryUsageBrush = Brushes.LightGreen;
            }
            else
            {
                MemoryUsageValue = "N/A";
                MemoryUsageText = "N/A";
                MemoryUsageBrush = Brushes.Gray;
            }

            CpuUsageValue = "0.0%";
            CpuUsageText = "LOW";
            CpuUsageBrush = Brushes.LightGreen;
            SystemStatusValue = "Optimal";
            SystemStatusText = "GOOD";
            SystemStatusBrush = Brushes.LightGreen;
        }
        catch
        {
            MemoryUsageValue = "N/A";
            MemoryUsageText = "N/A";
            CpuUsageValue = "N/A";
            CpuUsageText = "N/A";
            SystemStatusValue = "Unknown";
            SystemStatusText = "N/A";
        }
    }

    private sealed class BenchmarkContext
    {
        public ConfigService? ConfigService { get; set; }

        public RuriLibSettingsService? SettingsService { get; set; }

        public PluginRepository? PluginRepository { get; set; }

        public BotData? BotData { get; set; }
    }

    private sealed class BenchmarkResult
    {
        private BenchmarkResult(string name, TimeSpan duration, string details, bool success, bool skipped, string? errorMessage)
        {
            Name = name;
            Duration = duration;
            Details = details;
            Success = success;
            Skipped = skipped;
            ErrorMessage = errorMessage;
        }

        public string Name { get; }

        public TimeSpan Duration { get; }

        public string Details { get; }

        public bool Success { get; }

        public bool Skipped { get; }

        public string? ErrorMessage { get; }

        public static BenchmarkResult SuccessResult(string name, TimeSpan duration, string details)
            => new(name, duration, details, true, false, null);

        public static BenchmarkResult Failure(string name, TimeSpan duration, string error)
            => new(name, duration, string.Empty, false, false, error);

        public static BenchmarkResult SkippedResult(string name, string reason)
            => new(name, TimeSpan.Zero, reason, false, true, null);
    }
}
