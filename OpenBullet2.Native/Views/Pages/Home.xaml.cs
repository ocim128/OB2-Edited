using OpenBullet2.Core.Repositories;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;

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
    }

    public class HomeViewModel : ViewModelBase, IDisposable
    {
        private readonly AnnouncementService annService;
        private readonly IJobRepository jobRepo;
        private readonly IConfigRepository configRepo;
        private readonly IHitRepository hitRepo;
        private readonly IProxyGroupRepository proxyRepo;
        private readonly IWordlistRepository wordlistRepo;
        private readonly IGuestRepository guestRepo;
        private readonly DispatcherTimer refreshTimer;
        private readonly DispatcherTimer statisticsTimer;
        
        // Static application start time to persist across ViewModel instances
        private static readonly DateTime applicationStartTime = DateTime.Now;
        
        // Cache for statistics to reduce database queries
        private DateTime lastStatisticsUpdate = DateTime.MinValue;
        private readonly TimeSpan statisticsUpdateInterval = TimeSpan.FromSeconds(10); // Update stats every 10 seconds instead of 2
        
        private bool disposed = false;



        private string announcement = "Loading announcement...";
        public string Announcement
        {
            get => announcement;
            set
            {
                announcement = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAnnouncement));
            }
        }

        public bool HasAnnouncement => !string.IsNullOrWhiteSpace(announcement) && announcement != "Loading announcement...";

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
        public string OperatingSystem => RuntimeInformation.OSDescription;
        public string DotNetVersion => RuntimeInformation.FrameworkDescription;
        public string ApplicationVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        public string WorkingDirectory => Directory.GetCurrentDirectory();
        public string WorkingDirectoryShort => Path.GetFileName(Directory.GetCurrentDirectory()) ?? "Unknown";
        public string BuildDate => File.GetCreationTime(System.Reflection.Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd HH:mm");
        
        private string currentTime = DateTime.Now.ToString("ddd, MMM dd, yyyy h:mm tt");
        public string CurrentTime
        {
            get => currentTime;
            set
            {
                currentTime = value;
                OnPropertyChanged();
            }
        }
        
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
                annService = SP.GetService<AnnouncementService>();
                jobRepo = SP.GetService<IJobRepository>();
                configRepo = SP.GetService<IConfigRepository>();
                hitRepo = SP.GetService<IHitRepository>();
                proxyRepo = SP.GetService<IProxyGroupRepository>();
                wordlistRepo = SP.GetService<IWordlistRepository>();
                guestRepo = SP.GetService<IGuestRepository>();

                // Fetch announcement and load initial statistics
                FetchAnnouncement();
                LoadCollectionStatistics();
                
                // Setup fast refresh timer for system metrics (every 2 seconds)
                refreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                refreshTimer.Tick += (s, e) => 
                {
                    UpdateApplicationUptime();
                    UpdateMemoryUsage();
                    UpdateCurrentTime();
                    UpdateCpuUsage();
                    UpdateThreadCount();
                    
                    // Update statistics less frequently to reduce database load
                    if (DateTime.Now - lastStatisticsUpdate > statisticsUpdateInterval)
                    {
                        LoadCollectionStatistics();
                        lastStatisticsUpdate = DateTime.Now;
                    }
                };
                refreshTimer.Start();
                
                // Mark initial statistics as updated
                lastStatisticsUpdate = DateTime.Now;
            }
            catch (Exception ex)
            {
                // If service initialization fails, set default values
                Announcement = "Failed to load services: " + ex.Message;
                System.Diagnostics.Debug.WriteLine($"HomeViewModel initialization error: {ex}");
            }
            
            // Initial updates (always do these)
            UpdateApplicationUptime();
            UpdateMemoryUsage();
            UpdateCurrentTime();
            UpdateCpuUsage();
            UpdateThreadCount();
        }

        private async void FetchAnnouncement()
        {
            try
            {
                Announcement = await annService.FetchAnnouncementAsync();
            }
            catch
            {
                Announcement = ""; // Hide announcement section if fetch fails
            }
        }

        private async void LoadCollectionStatistics()
        {
            try
            {
                // Use Task.Run to offload database queries to background thread
                await Task.Run(async () =>
                {
                    // Parallel execution of independent database queries for better performance
                    var jobCountTask = Task.Run(() => jobRepo.GetAll().CountAsync());
                    var configCountTask = Task.Run(() => configRepo.GetAllAsync().ContinueWith(t => t.Result.Count()));
                    var hitCountTask = Task.Run(() => hitRepo.CountAsync());
                    var guestCountTask = Task.Run(() => guestRepo.GetAll().CountAsync());
                    var proxyCountTask = CountProxiesAsync();
                    var wordlistTask = CountWordlistsAsync();
                    var pluginCountTask = Task.Run(() => CountPlugins());
                    
                    await Task.WhenAll(jobCountTask, configCountTask, hitCountTask, guestCountTask, proxyCountTask, wordlistTask, pluginCountTask);
                    
                    // Update UI on main thread
                    Application.Current.Dispatcher.Invoke(() =>
                    {
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
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadCollectionStatistics error: {ex}");
                
                // If any repository call fails, set counts to 0
                JobsCount = 0;
                ConfigsCount = 0;
                HitsCount = 0;
                ProxiesCount = 0;
                WordlistsCount = 0;
                wordlistLines = 0;
                OnPropertyChanged(nameof(WordlistLines));
                GuestsCount = 0;
                PluginsCount = 0;
            }
        }
        
        private async Task<long> CountProxiesAsync()
        {
            try
            {
                var proxyGroups = await Task.Run(() => proxyRepo.GetAll().Include(g => g.Proxies).ToListAsync());
                return proxyGroups.Sum(group => group.Proxies?.Count ?? 0);
            }
            catch
            {
                return 0;
            }
        }
        
        private async Task<(int count, long lines)> CountWordlistsAsync()
        {
            try
            {
                var wordlists = await Task.Run(() => wordlistRepo.GetAll().ToListAsync());
                var count = wordlists.Count;
                var totalLines = wordlists.Sum(w => w.Total);
                return (count, totalLines);
            }
            catch
            {
                return (0, 0);
            }
        }
        
        private int CountPlugins()
        {
            try
            {
                var pluginsDir = Path.Combine(Directory.GetCurrentDirectory(), "Plugins");
                return Directory.Exists(pluginsDir) ? Directory.GetFiles(pluginsDir, "*.dll").Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private void UpdateApplicationUptime()
        {
            var uptime = DateTime.Now - applicationStartTime;
            ApplicationUptime = $"{uptime.Days:D2}d {uptime.Hours:D2}h {uptime.Minutes:D2}m {uptime.Seconds:D2}s";
        }

        private void UpdateMemoryUsage()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                // Use PrivateMemorySize64 for more accurate memory usage (matches Task Manager better)
                var memoryBytes = process.PrivateMemorySize64;
                MemoryUsage = FormatBytes(memoryBytes);
            }
            catch
            {
                MemoryUsage = "N/A";
            }
        }
        
        private void UpdateCurrentTime()
        {
            CurrentTime = DateTime.Now.ToString("ddd, MMM dd, yyyy h:mm tt");
        }
        
        private DateTime lastCpuTime = DateTime.UtcNow;
        private TimeSpan lastTotalProcessorTime = TimeSpan.Zero;
        private bool firstCpuMeasurement = true;
        private readonly int processorCount = Environment.ProcessorCount;
        
        private void UpdateCpuUsage()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                var currentTime = DateTime.UtcNow;
                var currentTotalProcessorTime = process.TotalProcessorTime;

                if (firstCpuMeasurement)
                {
                    lastCpuTime = currentTime;
                    lastTotalProcessorTime = currentTotalProcessorTime;
                    firstCpuMeasurement = false;
                    CpuUsage = "0.00%";
                    return;
                }

                var timeDiff = currentTime - lastCpuTime;
                var processorTimeDiff = currentTotalProcessorTime - lastTotalProcessorTime;

                if (timeDiff.TotalMilliseconds > 100) // Minimum interval to avoid division by very small numbers
                {
                    // Calculate CPU usage per core (matches Task Manager calculation)
                    var cpuPercent = (processorTimeDiff.TotalMilliseconds / timeDiff.TotalMilliseconds / processorCount) * 100;
                    CpuUsage = $"{Math.Min(cpuPercent, 100.0):F2}%";
                    
                    lastCpuTime = currentTime;
                    lastTotalProcessorTime = currentTotalProcessorTime;
                }
                // If time diff is too small, keep previous value to avoid noise
            }
            catch
            {
                CpuUsage = "N/A";
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
                // DispatcherTimer doesn't implement IDisposable, just stop it
                disposed = true;
            }
        }
    }
}
