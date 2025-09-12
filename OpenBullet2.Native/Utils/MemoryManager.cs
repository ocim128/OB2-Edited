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
                // Prefer Interactive for UI apps; avoid SustainedLowLatency which can starve GC.
                GCSettings.LatencyMode = GCLatencyMode.Interactive;

                // Do a single optimized collection at startup; avoid multiple forced collections.
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
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
            // Throttle collections by time and memory pressure
            var shouldCollect = false;
            lock (_lockObject)
            {
                var now = DateTime.UtcNow;
                if (now - _lastGcTime >= _minGcInterval && IsMemoryPressureHigh())
                {
                    _lastGcTime = now;
                    shouldCollect = true;
                }
            }

            if (!shouldCollect) return;

            // Use a single background task without unbounded fan-out
            Task.Run(() =>
            {
                try
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
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

                var computerInfo = new ComputerInfo();
                var totalMemory = computerInfo.TotalPhysicalMemory;
                var availableMemory = computerInfo.AvailablePhysicalMemory;

                if (totalMemory == 0) return false;

                // Calculate percentage-based thresholds
                var usedPercent = (double)(totalMemory - availableMemory) / totalMemory * 100.0;
                var managedPercent = (double)managedMemory / totalMemory * 100.0;

                // Trigger when overall memory is high or managed heap proportion is excessive relative to system RAM
                return usedPercent >= 85.0 || managedPercent >= 40.0 || workingSet >= totalMemory * 0.85;
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

                if (_lastCpuTime != DateTime.MinValue)
                {
                    var cpuUsedMs = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
                    var totalMsPassed = (now - _lastCpuTime).TotalMilliseconds;
                    if (totalMsPassed > 1)
                    {
                        var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                        _lastCpuUsage = (float)Math.Clamp(cpuUsageTotal * 100.0, 0.0, 100.0);
                    }
                }

                _lastTotalProcessorTime = currentTotalProcessorTime;
                _lastCpuTime = now;

                return _lastCpuUsage;
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

                var computerInfo = new ComputerInfo();
                var totalMemory = (long)computerInfo.TotalPhysicalMemory;
                var availableMemory = (long)computerInfo.AvailablePhysicalMemory;
                var usagePercent = totalMemory == 0 ? 0f : (float)(100.0 * (totalMemory - availableMemory) / totalMemory);

                _lastMemoryInfo = (totalMemory, availableMemory, usagePercent);
                _lastMemoryTime = now;

                return _lastMemoryInfo;
            }
            catch
            {
                return (0, 0, 0f);
            }
        }

        /// <summary>
        /// Gets application-specific memory information
        /// </summary>
        private static DateTime _lastAppMemoryTime = DateTime.MinValue;
        private static (long workingSet, long managedMemory, float systemPercent) _lastAppMemoryInfo;
        private static readonly TimeSpan _appMemoryUpdateInterval = TimeSpan.FromSeconds(2);

        public static (long workingSetBytes, long managedMemoryBytes, float systemUsagePercent) GetApplicationMemoryInfo()
        {
            try
            {
                var now = DateTime.UtcNow;
                if (now - _lastAppMemoryTime < _appMemoryUpdateInterval)
                {
                    return _lastAppMemoryInfo;
                }

                using var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64;
                var managedMemory = GC.GetTotalMemory(false);

                var computerInfo = new ComputerInfo();
                var totalSystemMemory = (long)computerInfo.TotalPhysicalMemory;
                var systemUsagePercent = totalSystemMemory == 0 ? 0f : (float)(100.0 * workingSet / totalSystemMemory);

                _lastAppMemoryInfo = (workingSet, managedMemory, systemUsagePercent);
                _lastAppMemoryTime = now;

                return _lastAppMemoryInfo;
            }
            catch
            {
                return (0, 0, 0f);
            }
        }
    }
}
