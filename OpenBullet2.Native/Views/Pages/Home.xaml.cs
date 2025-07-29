using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Infrastructure.DependencyInjection;
using OpenBullet2.Native.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using static OpenBullet2.Native.MainWindow;

namespace OpenBullet2.Native.Views.Pages
{
    /// <summary>
    /// Interaction logic for Home.xaml
    /// </summary>
    public partial class Home : Page
    {
        private readonly HomeViewModel vm;

        public Home()
        {
            InitializeComponent();

            vm = new HomeViewModel();
            DataContext = vm;

            // Cleanup when page is unloaded
            Unloaded += (s, e) => vm?.Dispose();
        }

        private void ConfigsShortcut_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            mainWindow?.NavigateTo(MainWindowPage.Configs);
        }

        private void JobsShortcut_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            mainWindow?.NavigateTo(MainWindowPage.Jobs);
        }


    }

    public class HomeViewModel : ViewModelBase, IDisposable
    {
        private readonly IJobRepository jobRepo;
        private readonly IConfigRepository configRepo;
        private readonly IHitRepository hitRepo;
        private readonly IProxyGroupRepository proxyRepo;
        private readonly IWordlistRepository wordlistRepo;
        private readonly IGuestRepository guestRepo;
        private readonly DispatcherTimer refreshTimer;
        private readonly DispatcherTimer statisticsTimer;

        // Static application start time to persist across ViewModel instances
        private static readonly DateTime applicationStartTime = DateTime.UtcNow;

        // Cache for statistics to reduce database queries
        private DateTime lastStatisticsUpdate = DateTime.MinValue;
        private readonly TimeSpan statisticsUpdateInterval;
        private readonly TimeSpan systemMetricsUpdateInterval;

        // Performance optimization flags
        private readonly bool isLowSpecMode;
        private readonly bool enableSmoothing;
        private int updateCounter = 0;

        private bool disposed = false;





        // Collection Statistics
        private int jobsCount;
        public int JobsCount
        {
            get => jobsCount;
            set
            {
                jobsCount = value;
                OnPropertyChanged();
            }
        }

        private int configsCount;
        public int ConfigsCount
        {
            get => configsCount;
            set
            {
                configsCount = value;
                OnPropertyChanged();
            }
        }

        private int hitsCount;
        public int HitsCount
        {
            get => hitsCount;
            set
            {
                hitsCount = value;
                OnPropertyChanged();
            }
        }

        private int proxiesCount;
        public int ProxiesCount
        {
            get => proxiesCount;
            set
            {
                proxiesCount = value;
                OnPropertyChanged();
            }
        }

        private int wordlistsCount;
        public int WordlistsCount
        {
            get => wordlistsCount;
            set
            {
                wordlistsCount = value;
                OnPropertyChanged();
            }
        }

        private long wordlistLines;
        public string WordlistLines
        {
            get => FormatNumber(wordlistLines);
            set
            {
                if (long.TryParse(value, out var parsed))
                {
                    wordlistLines = parsed;
                    OnPropertyChanged();
                }
            }
        }

        private int guestsCount;
        public int GuestsCount
        {
            get => guestsCount;
            set
            {
                guestsCount = value;
                OnPropertyChanged();
            }
        }

        private int pluginsCount;
        public int PluginsCount
        {
            get => pluginsCount;
            set
            {
                pluginsCount = value;
                OnPropertyChanged();
            }
        }

        // System Information
        public static string OperatingSystem => RuntimeInformation.OSDescription;
        public static string DotNetVersion => RuntimeInformation.FrameworkDescription;
        public static string ApplicationVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        public static string WorkingDirectory => Directory.GetCurrentDirectory();
        public static string WorkingDirectoryShort => Path.GetFileName(Directory.GetCurrentDirectory()) ?? "Unknown";
        public static string BuildDate => File.GetCreationTime(System.Reflection.Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd HH:mm");



        private string applicationUptime = "00:00:00";
        public string ApplicationUptime
        {
            get => applicationUptime;
            set
            {
                applicationUptime = value;
                OnPropertyChanged();
            }
        }

        private string memoryUsage = "0 MB";
        public string MemoryUsage
        {
            get => memoryUsage;
            set
            {
                memoryUsage = value;
                OnPropertyChanged();
            }
        }

        private float memoryUsagePercent = 0f;
        public float MemoryUsagePercent
        {
            get => memoryUsagePercent;
            set
            {
                memoryUsagePercent = value;
                OnPropertyChanged();
            }
        }

        private string cpuUsage = "0.00%";
        public string CpuUsage
        {
            get => cpuUsage;
            set
            {
                cpuUsage = value;
                OnPropertyChanged();
            }
        }



        private int threadCount = 0;
        public int ThreadCount
        {
            get => threadCount;
            set
            {
                threadCount = value;
                OnPropertyChanged();
            }
        }

        public HomeViewModel()
        {
            try
            {
                jobRepo = ServiceLocator.GetService<IJobRepository>();
                configRepo = ServiceLocator.GetService<IConfigRepository>();
                hitRepo = ServiceLocator.GetService<IHitRepository>();
                proxyRepo = ServiceLocator.GetService<IProxyGroupRepository>();
                wordlistRepo = ServiceLocator.GetService<IWordlistRepository>();
                guestRepo = ServiceLocator.GetService<IGuestRepository>();

                // Get performance settings from configuration
                var config = ServiceLocator.GetService<IConfiguration>();
                var performanceSection = config.GetSection("Performance");
                var statisticsInterval = performanceSection.GetValue<int>("StatisticsUpdateInterval", 45);
                var systemMetricsInterval = performanceSection.GetValue<int>("SystemMetricsUpdateInterval", 8);

                // Performance optimization settings
                isLowSpecMode = performanceSection.GetValue<bool>("LowSpecMode", false);
                enableSmoothing = performanceSection.GetValue<bool>("EnableSmoothing", true);

                // Adjust intervals for low-spec mode
                if (isLowSpecMode)
                {
                    statisticsInterval = Math.Max(statisticsInterval, 60); // Minimum 60s for low-spec
                    systemMetricsInterval = Math.Max(systemMetricsInterval, 10); // Minimum 10s for low-spec
                }

                statisticsUpdateInterval = TimeSpan.FromSeconds(statisticsInterval);
                systemMetricsUpdateInterval = TimeSpan.FromSeconds(systemMetricsInterval);

                // Load initial statistics
                _ = Task.Run(async () => await LoadCollectionStatistics());

                // Setup optimized refresh timer for system metrics
                refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = systemMetricsUpdateInterval
                };
                refreshTimer.Tick += OnSystemMetricsTimerTick;

                // Setup separate timer for statistics to avoid blocking UI updates
                statisticsTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = statisticsUpdateInterval
                };
                statisticsTimer.Tick += OnStatisticsTimerTick;

                refreshTimer.Start();
                statisticsTimer.Start();

                // Initial updates with staggered execution
                UpdateApplicationUptime();

                // Delay heavy operations to avoid startup lag
                _ = Task.Delay(1000).ContinueWith(_ =>
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        UpdateMemoryUsage();
                        UpdateThreadCount();
                    }, DispatcherPriority.Background);

                    _ = Task.Run(async () =>
                    {
                        var cpuUsage = await CalculateCpuUsage();
                        CpuUsage = $"{cpuUsage:F2}%";
                    });
                });
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }



        private async Task LoadCollectionStatistics()
        {
            try
            {
                // Skip if recently updated to reduce database load
                if (DateTime.UtcNow - lastStatisticsUpdate < TimeSpan.FromSeconds(30))
                {
                    return;
                }

                // Check if repositories are available
                if (jobRepo == null || configRepo == null || hitRepo == null ||
                    guestRepo == null || wordlistRepo == null || proxyRepo == null)
                {
                    System.Diagnostics.Debug.WriteLine("One or more repositories are null, skipping statistics update");
                    return;
                }

                // Get timeout setting from configuration
                var config = ServiceLocator.GetService<IConfiguration>();
                var timeoutSeconds = config?.GetSection("Performance")?.GetValue<int>("DatabaseQueryTimeout", 10) ?? 10;
                var timeout = TimeSpan.FromSeconds(isLowSpecMode ? 15 : timeoutSeconds);

                using var cts = new System.Threading.CancellationTokenSource(timeout);

                // Use Task.Run to offload database queries to background thread
                await Task.Run(async () =>
                {
                    // Prioritize most important statistics for low-spec mode
                    if (isLowSpecMode)
                    {
                        // Only update essential statistics in low-spec mode
                        var jobCountTask = Task.Run(async () =>
                        {
                            try { return await jobRepo.GetAll().CountAsync(cts.Token); }
                            catch { return 0; }
                        }, cts.Token);

                        var configCountTask = Task.Run(async () =>
                        {
                            try { return (await configRepo.GetAllAsync())?.Count() ?? 0; }
                            catch { return 0; }
                        }, cts.Token);

                        var hitCountTask = Task.Run(async () =>
                        {
                            try { return await hitRepo.CountAsync(); }
                            catch { return 0L; }
                        }, cts.Token);

                        var pluginCountTask = Task.Run(() => CountPlugins(), cts.Token);

                        await Task.WhenAll(jobCountTask, configCountTask, hitCountTask, pluginCountTask);

                        // Update UI on main thread with null checks
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (disposed) return;

                            JobsCount = jobCountTask.Result;
                            ConfigsCount = configCountTask.Result;
                            HitsCount = (int)Math.Min(hitCountTask.Result, int.MaxValue);
                            PluginsCount = pluginCountTask.Result;
                            // Keep previous values for other properties to reduce UI updates
                        });
                    }
                    else
                    {
                        // Full statistics update for normal mode
                        var jobCountTask = Task.Run(async () =>
                        {
                            try { return await jobRepo.GetAll().CountAsync(cts.Token); }
                            catch { return 0; }
                        }, cts.Token);

                        var configCountTask = Task.Run(async () =>
                        {
                            try { return (await configRepo.GetAllAsync())?.Count() ?? 0; }
                            catch { return 0; }
                        }, cts.Token);

                        var hitCountTask = Task.Run(async () =>
                        {
                            try { return await hitRepo.CountAsync(); }
                            catch { return 0L; }
                        }, cts.Token);

                        var guestCountTask = Task.Run(async () =>
                        {
                            try { return await guestRepo.GetAll().CountAsync(cts.Token); }
                            catch { return 0; }
                        }, cts.Token);

                        var proxyCountTask = CountProxiesAsync(cts.Token);
                        var wordlistTask = CountWordlistsAsync(cts.Token);
                        var pluginCountTask = Task.Run(() => CountPlugins(), cts.Token);

                        await Task.WhenAll(jobCountTask, configCountTask, hitCountTask, guestCountTask, proxyCountTask, wordlistTask, pluginCountTask);

                        // Update UI on main thread with null checks
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (disposed) return;

                            JobsCount = jobCountTask.Result;
                            ConfigsCount = configCountTask.Result;
                            HitsCount = (int)Math.Min(hitCountTask.Result, int.MaxValue);
                            GuestsCount = (int)guestCountTask.Result;
                            ProxiesCount = (int)proxyCountTask.Result;

                            var wordlistResult = wordlistTask.Result;
                            WordlistsCount = wordlistResult.count;
                            wordlistLines = wordlistResult.lines;
                            OnPropertyChanged(nameof(WordlistLines));

                            PluginsCount = pluginCountTask.Result;
                        });
                    }

                    lastStatisticsUpdate = DateTime.UtcNow;
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("LoadCollectionStatistics timed out - using cached values");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadCollectionStatistics error: {ex.Message}");

                // If any repository call fails, set counts to 0
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (disposed) return;

                    JobsCount = 0;
                    ConfigsCount = 0;
                    HitsCount = 0;
                    ProxiesCount = 0;
                    WordlistsCount = 0;
                    wordlistLines = 0;
                    OnPropertyChanged(nameof(WordlistLines));
                    GuestsCount = 0;
                    PluginsCount = 0;
                });
            }
        }



        private async Task<long> CountProxiesAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                var proxyGroups = await Task.Run(() => proxyRepo.GetAll().Include(g => g.Proxies).ToListAsync(cancellationToken), cancellationToken);
                return proxyGroups.Sum(group => group.Proxies?.Count ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        private async Task<(int count, long lines)> CountWordlistsAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                var wordlists = await Task.Run(() => wordlistRepo.GetAll().ToListAsync(cancellationToken), cancellationToken);
                var count = wordlists.Count;
                var totalLines = wordlists.Sum(w => w.Total);
                return (count, totalLines);
            }
            catch
            {
                return (0, 0);
            }
        }

        private static int CountPlugins()
        {
            var pluginDir = Path.Combine(Directory.GetCurrentDirectory(), "Plugins");
            return Directory.Exists(pluginDir)
                ? Directory.GetFiles(pluginDir, "*.dll").Length
                : 0;
        }

        private void UpdateApplicationUptime()
        {
            ApplicationUptime = (DateTime.UtcNow - applicationStartTime).ToString(@"hh\:mm\:ss");
        }

        private void UpdateMemoryUsage()
        {
            try
            {
                var (workingSet, managedMemory, systemPercent) = MemoryManager.GetApplicationMemoryInfo();
                MemoryUsage = $"{MemoryManager.FormatMemorySize(workingSet)}";
                MemoryUsagePercent = systemPercent;
            }
            catch
            {
                MemoryUsage = "N/A";
                MemoryUsagePercent = 0f;
            }
        }



        private async Task<double> CalculateCpuUsage()
        {
            try
            {
                // Use cached value for low-spec mode to reduce overhead
                if (isLowSpecMode)
                {
                    return await Task.Run(() =>
                    {
                        // Simplified CPU calculation for low-spec systems
                        var process = System.Diagnostics.Process.GetCurrentProcess();
                        var startTime = DateTime.UtcNow;
                        var startCpuUsage = process.TotalProcessorTime;

                        // Reduced delay for more responsive measurements
                        System.Threading.Thread.Sleep(100);

                        var endTime = DateTime.UtcNow;
                        var endCpuUsage = process.TotalProcessorTime;

                        var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                        var totalMsPassed = (endTime - startTime).TotalMilliseconds;

                        if (totalMsPassed <= 0)
                            return 0.0;

                        var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

                        return Math.Min(cpuUsageTotal * 100.0, 100.0);
                    }).ConfigureAwait(false);
                }
                else
                {
                    return await Task.Run(() => MemoryManager.GetSystemCpuUsage()).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating CPU usage: {ex.Message}");
                return 0.0;
            }
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
                return $"{number / 1_000_000_000.0:F1}B";
            if (number >= 1_000_000)
                return $"{number / 1_000_000.0:F1}M";
            if (number >= 1_000)
                return $"{number / 1_000.0:F1}K";
            return number.ToString();
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:F1} {sizes[order]}";
        }

        private async void OnSystemMetricsTimerTick(object sender, EventArgs e)
        {
            try
            {
                updateCounter++;

                // Always update uptime (lightweight)
                UpdateApplicationUptime();

                // Throttle heavy operations for low-spec mode
                bool shouldUpdateHeavyMetrics = !isLowSpecMode || (updateCounter % 2 == 0);

                if (shouldUpdateHeavyMetrics)
                {
                    UpdateMemoryUsage();
                    UpdateThreadCount();

                    // CPU calculation is expensive, do it less frequently
                    if (updateCounter % 3 == 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var cpuUsage = await CalculateCpuUsage().ConfigureAwait(false);
                                Application.Current?.Dispatcher.BeginInvoke(() =>
                                {
                                    CpuUsage = $"{cpuUsage:F2}%";
                                }, DispatcherPriority.Background);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"CPU calculation error: {ex.Message}");
                            }
                        });
                    }
                }

                // Memory pressure check (less frequent for low-spec)
                if (updateCounter % (isLowSpecMode ? 6 : 4) == 0)
                {
                    if (MemoryManager.IsMemoryPressureHigh())
                    {
                        _ = Task.Run(() => MemoryManager.TryCollectGarbage());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"System metrics timer error: {ex.Message}");
            }
        }

        private async void OnStatisticsTimerTick(object sender, EventArgs e)
        {
            try
            {
                // Run statistics update in background to avoid UI blocking
                _ = Task.Run(async () => await LoadCollectionStatistics());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Statistics timer error: {ex.Message}");
            }
        }



        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {
                refreshTimer?.Stop();
                statisticsTimer?.Stop();

                // Clear event handlers to prevent memory leaks
                refreshTimer.Tick -= OnSystemMetricsTimerTick;
                statisticsTimer.Tick -= OnStatisticsTimerTick;

                disposed = true;
            }
        }
    }
}
