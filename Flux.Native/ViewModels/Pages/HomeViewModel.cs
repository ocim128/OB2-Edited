using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Flux.Native.Utils;
using Flux.Native.ViewModels.Base;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;

namespace Flux.Native.ViewModels.Pages;

public class HomeViewModel : ViewModelBase, IDisposable
{
    private readonly IDashboardService _dashboardService;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _statisticsTimer;
    private readonly DesktopDashboardRefreshOptionsDto _refreshOptions;

    private static readonly DateTime applicationStartTime = DateTime.UtcNow;

    private DateTime lastStatisticsUpdate = DateTime.MinValue;
    private int updateCounter;
    private bool disposed;

    public HomeViewModel(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
        _refreshOptions = _dashboardService.GetDesktopRefreshOptions();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = _refreshOptions.SystemMetricsInterval
        };
        _refreshTimer.Tick += OnSystemMetricsTimerTick;

        _statisticsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = _refreshOptions.StatisticsInterval
        };
        _statisticsTimer.Tick += OnStatisticsTimerTick;
    }

    public int JobsCount
    {
        get => jobsCount;
        set => SetProperty(ref jobsCount, value);
    }
    private int jobsCount;

    public int ConfigsCount
    {
        get => configsCount;
        set => SetProperty(ref configsCount, value);
    }
    private int configsCount;

    public int HitsCount
    {
        get => hitsCount;
        set => SetProperty(ref hitsCount, value);
    }
    private int hitsCount;

    public int ProxiesCount
    {
        get => proxiesCount;
        set => SetProperty(ref proxiesCount, value);
    }
    private int proxiesCount;

    public int WordlistsCount
    {
        get => wordlistsCount;
        set => SetProperty(ref wordlistsCount, value);
    }
    private int wordlistsCount;

    public string WordlistLines => FormatNumber(wordlistLines);
    private long wordlistLines;

    public int GuestsCount
    {
        get => guestsCount;
        set => SetProperty(ref guestsCount, value);
    }
    private int guestsCount;

    public int PluginsCount
    {
        get => pluginsCount;
        set => SetProperty(ref pluginsCount, value);
    }
    private int pluginsCount;

    public static string OperatingSystem => RuntimeInformation.OSDescription;
    public static string DotNetVersion => RuntimeInformation.FrameworkDescription;
    public static string ApplicationVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
    public static string WorkingDirectory => Directory.GetCurrentDirectory();
    public static string WorkingDirectoryShort => Path.GetFileName(Directory.GetCurrentDirectory()) ?? "Unknown";
    public static string BuildDate => File.GetCreationTime(System.Reflection.Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd HH:mm");

    public string ApplicationUptime
    {
        get => applicationUptime;
        set => SetProperty(ref applicationUptime, value);
    }
    private string applicationUptime = "00:00:00";

    public string MemoryUsage
    {
        get => memoryUsage;
        set => SetProperty(ref memoryUsage, value);
    }
    private string memoryUsage = "0 MB";

    public float MemoryUsagePercent
    {
        get => memoryUsagePercent;
        set => SetProperty(ref memoryUsagePercent, value);
    }
    private float memoryUsagePercent;

    public string CpuUsage
    {
        get => cpuUsage;
        set => SetProperty(ref cpuUsage, value);
    }
    private string cpuUsage = "0%";

    public float CpuUsagePercent
    {
        get => cpuUsagePercent;
        set => SetProperty(ref cpuUsagePercent, value);
    }
    private float cpuUsagePercent;

    public int ThreadCount
    {
        get => threadCount;
        set => SetProperty(ref threadCount, value);
    }
    private int threadCount;

    public void Resume()
    {
        if (disposed)
        {
            return;
        }

        UpdateApplicationUptime();
        UpdateMemoryUsage();
        UpdateThreadCount();

        _ = RefreshStatisticsAsync();
        _ = RefreshCpuUsageAsync();

        _refreshTimer.Start();
        _statisticsTimer.Start();
    }

    public void Suspend()
    {
        if (disposed)
        {
            return;
        }

        _refreshTimer.Stop();
        _statisticsTimer.Stop();
    }

    private async Task RefreshStatisticsAsync()
    {
        try
        {
            if (DateTime.UtcNow - lastStatisticsUpdate < TimeSpan.FromSeconds(30))
            {
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_refreshOptions.DatabaseQueryTimeoutSeconds));
            var snapshot = await _dashboardService.GetDesktopSnapshotAsync(cts.Token).ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (disposed)
                {
                    return;
                }

                JobsCount = snapshot.JobsCount;
                ConfigsCount = snapshot.ConfigsCount;
                HitsCount = snapshot.HitsCount;
                ProxiesCount = snapshot.ProxiesCount;
                WordlistsCount = snapshot.WordlistsCount;
                wordlistLines = snapshot.WordlistLines;
                OnPropertyChanged(nameof(WordlistLines));
                GuestsCount = snapshot.GuestsCount;
                PluginsCount = snapshot.PluginsCount;
            });

            lastStatisticsUpdate = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("Home dashboard refresh timed out");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Home dashboard refresh failed: {ex.Message}");
        }
    }

    private void UpdateApplicationUptime()
        => ApplicationUptime = (DateTime.UtcNow - applicationStartTime).ToString(@"hh\:mm\:ss");

    private void UpdateMemoryUsage()
    {
        try
        {
            var (workingSet, _, systemPercent) = MemoryManager.GetApplicationMemoryInfo();
            MemoryUsage = MemoryManager.FormatMemorySize(workingSet);
            MemoryUsagePercent = systemPercent;
        }
        catch
        {
            MemoryUsage = "N/A";
            MemoryUsagePercent = 0f;
        }
    }

    private async Task<double> CalculateCpuUsageAsync()
    {
        try
        {
            if (_refreshOptions.IsLowSpecMode)
            {
                return await Task.Run(() =>
                {
                    using var process = Process.GetCurrentProcess();
                    var startTime = DateTime.UtcNow;
                    var startCpuUsage = process.TotalProcessorTime;

                    Thread.Sleep(100);

                    var endTime = DateTime.UtcNow;
                    var endCpuUsage = process.TotalProcessorTime;
                    var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                    var totalMsPassed = (endTime - startTime).TotalMilliseconds;

                    if (totalMsPassed <= 0)
                    {
                        return 0.0;
                    }

                    var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                    return Math.Min(cpuUsageTotal * 100.0, 100.0);
                }).ConfigureAwait(false);
            }

            return await Task.Run(() => (double)MemoryManager.GetSystemCpuUsage()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CPU calculation failed: {ex.Message}");
            return 0.0;
        }
    }

    private async Task RefreshCpuUsageAsync()
    {
        var cpuUsageValue = await CalculateCpuUsageAsync().ConfigureAwait(false);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (disposed)
            {
                return;
            }

            CpuUsage = $"{cpuUsageValue:F1}%";
            CpuUsagePercent = (float)cpuUsageValue;
        });
    }

    private void UpdateThreadCount()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            ThreadCount = process.Threads.Count;
        }
        catch
        {
            ThreadCount = 0;
        }
    }

    private static string FormatNumber(long number)
    {
        if (number >= 1_000_000_000)
        {
            return $"{number / 1_000_000_000.0:F1}B";
        }

        if (number >= 1_000_000)
        {
            return $"{number / 1_000_000.0:F1}M";
        }

        if (number >= 1_000)
        {
            return $"{number / 1_000.0:F1}K";
        }

        return number.ToString();
    }

    private void OnSystemMetricsTimerTick(object? sender, EventArgs e)
    {
        try
        {
            updateCounter++;

            UpdateApplicationUptime();

            var shouldUpdateHeavyMetrics = !_refreshOptions.IsLowSpecMode || updateCounter % 2 == 0;
            if (shouldUpdateHeavyMetrics)
            {
                UpdateMemoryUsage();
                UpdateThreadCount();

                if (updateCounter % 3 == 0)
                {
                    _ = RefreshCpuUsageAsync();
                }
            }

            if (updateCounter % (_refreshOptions.IsLowSpecMode ? 6 : 4) == 0
                && MemoryManager.IsMemoryPressureHigh())
            {
                _ = Task.Run(MemoryManager.TryCollectGarbage);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"System metrics timer error: {ex.Message}");
        }
    }

    private async void OnStatisticsTimerTick(object? sender, EventArgs e)
    {
        try
        {
            await RefreshStatisticsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Statistics timer error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        _refreshTimer.Stop();
        _statisticsTimer.Stop();
        _refreshTimer.Tick -= OnSystemMetricsTimerTick;
        _statisticsTimer.Tick -= OnStatisticsTimerTick;
    }
}
