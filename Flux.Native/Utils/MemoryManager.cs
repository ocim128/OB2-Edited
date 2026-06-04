using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Flux.Native.Helpers;

namespace Flux.Native.Utils
{
    /// <summary>
    /// Utility class for managing memory and garbage collection in low-spec environments
    /// </summary>
    public static class MemoryManager
    {
        private static readonly object _lockObject = new object();
        private static DateTime _lastGcTime = DateTime.MinValue;
        private static readonly TimeSpan _minGcInterval = TimeSpan.FromSeconds(30);
        private static readonly Process CurrentProcess = Process.GetCurrentProcess();

        // #COMPLETION_DRIVE: We replaced Microsoft.VisualBasic.Devices.ComputerInfo with
        // GlobalMemoryStatusEx P/Invoke so the process no longer needs the entire
        // Microsoft.VisualBasic assembly on its load path.
        // #SUGGEST_VERIFY: Confirm the "Total physical memory" / "Available physical memory"
        // values shown in the Home dashboard match the OS task manager or
        // `Get-CimInstance Win32_OperatingSystem` to within a few MB before trusting
        // pressure thresholds.

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private static bool TryGetPhysicalMemory(out long totalBytes, out long availableBytes)
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref status))
            {
                totalBytes = 0;
                availableBytes = 0;
                return false;
            }

            totalBytes = (long)status.ullTotalPhys;
            availableBytes = (long)status.ullAvailPhys;
            return true;
        }

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
                var workingSet = CurrentProcess.WorkingSet64;
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

                if (!TryGetPhysicalMemory(out var totalMemory, out var availableMemory) || totalMemory == 0)
                {
                    return false;
                }

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
        /// Formats memory size in human-readable format (delegates to HumanReadable.Bytes)
        /// </summary>
        public static string FormatMemorySize(long bytes) => HumanReadable.Bytes(bytes);

        /// <summary>
        /// Gets system-wide CPU usage percentage using lightweight WMI approach.
        /// Thread-safe via lock on _cpuLock.
        /// </summary>
        private static readonly object _cpuLock = new();
        private static DateTime _lastCpuTime = DateTime.MinValue;
        private static TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;
        private static float _lastCpuUsage = 0f;
        private static readonly TimeSpan _cpuUpdateInterval = TimeSpan.FromSeconds(2);

        public static float GetSystemCpuUsage()
        {
            lock (_cpuLock)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    if (now - _lastCpuTime < _cpuUpdateInterval)
                    {
                        return _lastCpuUsage;
                    }

                    var currentTotalProcessorTime = CurrentProcess.TotalProcessorTime;

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
        }

        /// <summary>
        /// Gets system-wide memory information using lightweight approach.
        /// Thread-safe via lock on _memoryLock.
        /// </summary>
        private static readonly object _memoryLock = new();
        private static DateTime _lastMemoryTime = DateTime.MinValue;
        private static (long total, long available, float percent) _lastMemoryInfo;
        private static readonly TimeSpan _memoryUpdateInterval = TimeSpan.FromSeconds(3);

        public static (long totalMemory, long availableMemory, float usagePercent) GetSystemMemoryInfo()
        {
            lock (_memoryLock)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    if (now - _lastMemoryTime < _memoryUpdateInterval)
                    {
                        return _lastMemoryInfo;
                    }

                    if (!TryGetPhysicalMemory(out var totalMemory, out var availableMemory))
                    {
                        return (0, 0, 0f);
                    }

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
        }

        /// <summary>
        /// Gets application-specific memory information.
        /// Thread-safe via lock on _appMemoryLock.
        /// </summary>
        private static readonly object _appMemoryLock = new();
        private static DateTime _lastAppMemoryTime = DateTime.MinValue;
        private static (long workingSet, long managedMemory, float systemPercent) _lastAppMemoryInfo;
        private static readonly TimeSpan _appMemoryUpdateInterval = TimeSpan.FromSeconds(2);

        public static (long workingSetBytes, long managedMemoryBytes, float systemUsagePercent) GetApplicationMemoryInfo()
        {
            lock (_appMemoryLock)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    if (now - _lastAppMemoryTime < _appMemoryUpdateInterval)
                    {
                        return _lastAppMemoryInfo;
                    }

                    var workingSet = CurrentProcess.WorkingSet64;
                    var managedMemory = GC.GetTotalMemory(false);

                    var systemUsagePercent = TryGetPhysicalMemory(out var totalSystemMemory, out _) && totalSystemMemory > 0
                        ? (float)(100.0 * workingSet / totalSystemMemory)
                        : 0f;

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

        /// <summary>
        /// Gets the current thread count with throttled updates.
        /// Thread-safe via lock on _threadCountLock.
        /// </summary>
        private static readonly object _threadCountLock = new();
        private static DateTime _lastThreadCountTime = DateTime.MinValue;
        private static int _lastThreadCount;
        private static readonly TimeSpan _threadCountUpdateInterval = TimeSpan.FromSeconds(2);

        public static int GetThreadCount()
        {
            lock (_threadCountLock)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    if (now - _lastThreadCountTime < _threadCountUpdateInterval)
                    {
                        return _lastThreadCount;
                    }

                    _lastThreadCount = CurrentProcess.Threads.Count;
                    _lastThreadCountTime = now;
                    return _lastThreadCount;
                }
                catch
                {
                    return _lastThreadCount;
                }
            }
        }
    }
}
