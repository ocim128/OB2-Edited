using System;
using System.Diagnostics;
using System.Runtime;
using System.Threading.Tasks;

using Microsoft.VisualBasic.Devices;

namespace OpenBullet2.Native.Utils
{
    /// <summary>
    /// Utility class for managing memory and garbage collection in low-spec environments
    /// </summary>
    public static class MemoryManager
    {
        private static readonly object _lockObject = new object();
        private static DateTime _lastGcTime = DateTime.MinValue;
        private static readonly TimeSpan _minGcInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Applies garbage collection optimizations for low-spec systems
        /// </summary>
        public static void ApplyGarbageCollectionOptimizations()
        {
            try
            {
                // Configure GC for low-latency scenarios
                GCSettings.LatencyMode = GCLatencyMode.Interactive;

                // Force initial garbage collections to reduce startup overhead
                GC.Collect(0, GCCollectionMode.Optimized);
                GC.Collect(1, GCCollectionMode.Optimized);
                GC.WaitForPendingFinalizers();

                Debug.WriteLine("Applied garbage collection optimizations for low-spec mode");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to apply GC optimizations: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs a controlled garbage collection if enough time has passed
        /// </summary>
        public static void TryCollectGarbage()
        {
            lock (_lockObject)
            {
                var now = DateTime.UtcNow;
                if (now - _lastGcTime < _minGcInterval)
                    return;

                _lastGcTime = now;
            }

            Task.Run(() =>
            {
                try
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                    GC.WaitForPendingFinalizers();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Background GC failed: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Gets current memory usage information
        /// </summary>
        public static (long workingSet, long managedMemory) GetMemoryUsage()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64;
                var managedMemory = GC.GetTotalMemory(false);

                return (workingSet, managedMemory);
            }
            catch
            {
                return (0, 0);
            }
        }

        /// <summary>
        /// Checks if the system is under memory pressure
        /// </summary>
        public static bool IsMemoryPressureHigh()
        {
            try
            {
                var (workingSet, managedMemory) = GetMemoryUsage();

                // More adaptive memory pressure detection based on available system memory
                var computerInfo = new ComputerInfo();
                var totalMemory = computerInfo.TotalPhysicalMemory;
                var availableMemory = computerInfo.AvailablePhysicalMemory;

                // Calculate percentage-based thresholds
                var memoryUsagePercent = (double)(totalMemory - availableMemory) / totalMemory * 100;
                var managedMemoryPercent = (double)managedMemory / totalMemory * 100;

                // Use percentage-based thresholds instead of fixed values
                return memoryUsagePercent > 85 || managedMemoryPercent > 50 || workingSet > totalMemory * 0.8;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Formats memory size in human-readable format
        /// </summary>
        public static string FormatMemorySize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F1} MB";

            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }

        /// <summary>
        /// Gets system-wide CPU usage percentage using lightweight WMI approach
        /// </summary>
        private static DateTime _lastCpuTime = DateTime.MinValue;
        private static TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;
        private static float _lastCpuUsage = 0f;
        private static readonly TimeSpan _cpuUpdateInterval = TimeSpan.FromSeconds(2);

        public static float GetSystemCpuUsage()
        {
            try
            {
                var now = DateTime.UtcNow;
                if (now - _lastCpuTime < _cpuUpdateInterval)
                {
                    return _lastCpuUsage;
                }

                using var process = Process.GetCurrentProcess();
                var currentTotalProcessorTime = process.TotalProcessorTime;
                var currentTime = now;

                if (_lastCpuTime != DateTime.MinValue)
                {
                    var cpuUsedMs = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
                    var totalMsPassed = (currentTime - _lastCpuTime).TotalMilliseconds;
                    var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                    _lastCpuUsage = (float)(cpuUsageTotal * 100);
                }

                _lastTotalProcessorTime = currentTotalProcessorTime;
                _lastCpuTime = currentTime;

                return Math.Min(100f, Math.Max(0f, _lastCpuUsage));
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Gets system-wide memory information using lightweight approach
        /// </summary>
        private static DateTime _lastMemoryTime = DateTime.MinValue;
        private static (long total, long available, float percent) _lastMemoryInfo;
        private static readonly TimeSpan _memoryUpdateInterval = TimeSpan.FromSeconds(3);

        public static (long totalMemory, long availableMemory, float usagePercent) GetSystemMemoryInfo()
        {
            try
            {
                var now = DateTime.UtcNow;
                if (now - _lastMemoryTime < _memoryUpdateInterval)
                {
                    return _lastMemoryInfo;
                }

                var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
                var totalMemory = (long)computerInfo.TotalPhysicalMemory;
                var availableMemory = (long)computerInfo.AvailablePhysicalMemory;
                var usagePercent = (float)(100.0 * (totalMemory - availableMemory) / totalMemory);

                _lastMemoryInfo = (totalMemory, availableMemory, usagePercent);
                _lastMemoryTime = now;

                return _lastMemoryInfo;
            }
            catch
            {
                return (0, 0, 0f);
            }
        }
    }
}