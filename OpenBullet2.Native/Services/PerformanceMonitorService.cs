using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenBullet2.Native.Utils;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace OpenBullet2.Native.Services
{
    /// <summary>
    /// Service for monitoring application performance and applying optimizations
    /// </summary>
    public class PerformanceMonitorService : IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<PerformanceMonitorService> _logger;
        private readonly Timer _monitoringTimer;
        private readonly bool _lowSpecMode;
        private readonly bool _enableGcOptimization;
        private bool _disposed = false;
        
        public PerformanceMonitorService(IConfiguration configuration, ILogger<PerformanceMonitorService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            
            var performanceSection = _configuration.GetSection("Performance");
            _lowSpecMode = performanceSection.GetValue("LowSpecMode", false);
            _enableGcOptimization = performanceSection.GetValue("EnableGarbageCollectionOptimization", false);
            
            if (_lowSpecMode)
            {
                // Start monitoring timer for low-spec systems (every 60 seconds)
                _monitoringTimer = new Timer(MonitorPerformance, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
                _logger.LogInformation("Performance monitoring started for low-spec mode");
            }
        }
        
        private void MonitorPerformance(object state)
        {
            try
            {
                var (workingSet, managedMemory) = MemoryManager.GetMemoryUsage();
                var process = Process.GetCurrentProcess();
                var threadCount = process.Threads.Count;
                
                _logger.LogDebug($"Performance metrics - Working Set: {MemoryManager.FormatMemorySize(workingSet)}, " +
                               $"Managed Memory: {MemoryManager.FormatMemorySize(managedMemory)}, " +
                               $"Threads: {threadCount}");
                
                // Apply optimizations if memory pressure is high
                if (MemoryManager.IsMemoryPressureHigh())
                {
                    _logger.LogWarning("High memory pressure detected, applying optimizations");
                    
                    if (_enableGcOptimization)
                    {
                        MemoryManager.TryCollectGarbage();
                    }
                    
                    // Reduce UI update frequency during high memory pressure
                    ReduceUIUpdateFrequency();
                }
                
                // Monitor thread count and warn if too high
                if (threadCount > 100)
                {
                    _logger.LogWarning($"High thread count detected: {threadCount}. Consider reducing concurrent operations.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during performance monitoring");
            }
        }
        
        private void ReduceUIUpdateFrequency()
        {
            try
            {
                // Reduce animation frame rate during high memory pressure
                Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    try
                    {
                        System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(
                            typeof(System.Windows.Media.Animation.Timeline),
                            new System.Windows.FrameworkPropertyMetadata { DefaultValue = 15 });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to reduce UI update frequency");
                    }
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reducing UI update frequency");
            }
        }
        
        /// <summary>
        /// Gets current performance metrics
        /// </summary>
        public PerformanceMetrics GetCurrentMetrics()
        {
            try
            {
                var (workingSet, managedMemory) = MemoryManager.GetMemoryUsage();
                var process = Process.GetCurrentProcess();
                
                return new PerformanceMetrics
                {
                    WorkingSetMemory = workingSet,
                    ManagedMemory = managedMemory,
                    ThreadCount = process.Threads.Count,
                    IsMemoryPressureHigh = MemoryManager.IsMemoryPressureHigh(),
                    Timestamp = DateTime.UtcNow
                };
            }
            catch
            {
                return new PerformanceMetrics
                {
                    Timestamp = DateTime.UtcNow
                };
            }
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _monitoringTimer?.Dispose();
                _disposed = true;
                _logger.LogInformation("Performance monitoring service disposed");
            }
        }
    }
    
    /// <summary>
    /// Performance metrics data structure
    /// </summary>
    public class PerformanceMetrics
    {
        public long WorkingSetMemory { get; set; }
        public long ManagedMemory { get; set; }
        public int ThreadCount { get; set; }
        public bool IsMemoryPressureHigh { get; set; }
        public DateTime Timestamp { get; set; }
    }
}