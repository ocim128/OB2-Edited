using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace OpenBullet2.Native.Infrastructure.Diagnostics
{
    /// <summary>
    /// Monitors assembly loading failures and dependency resolution issues
    /// that commonly cause silent crashes during application startup.
    /// </summary>
    public sealed class AssemblyLoadingMonitor
    {
        private readonly ConcurrentQueue<AssemblyLoadEvent> _loadEvents = new();
        private readonly ConcurrentDictionary<string, AssemblyLoadFailure> _failures = new();
        private readonly object _lockObject = new object();
        private readonly string _logPath;
        private bool _isMonitoring;
        private static readonly Lazy<AssemblyLoadingMonitor> _instance = new Lazy<AssemblyLoadingMonitor>(() => new AssemblyLoadingMonitor());
        
        public static AssemblyLoadingMonitor Instance => _instance.Value;
        
        private AssemblyLoadingMonitor()
        {
            try
            {
                var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var userDataPath = Path.Combine(appDirectory, "UserData");
                var logsRoot = Path.Combine(userDataPath, "Logs");
                var assemblyLogsDir = Path.Combine(logsRoot, "Assembly");
                Directory.CreateDirectory(assemblyLogsDir);
                
                _logPath = Path.Combine(assemblyLogsDir, $"assembly-loading-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            }
            catch
            {
                _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assembly-loading-fallback.log");
            }
        }
        
        public void StartMonitoring()
        {
            if (_isMonitoring) return;
            
            lock (_lockObject)
            {
                if (_isMonitoring) return;
                
                try
                {
                    // Hook into assembly resolution events
                    AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                    AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                    AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += OnReflectionOnlyAssemblyResolve;
                    
                    _isMonitoring = true;
                    
                    LogEvent("Assembly loading monitoring started", AssemblyLoadEventType.MonitoringStarted);
                    
                    // Log currently loaded assemblies
                    LogCurrentlyLoadedAssemblies();
                }
                catch (Exception ex)
                {
                    LogEvent($"Failed to start assembly monitoring: {ex.Message}", AssemblyLoadEventType.MonitoringError);
                }
            }
        }
        
        public void StopMonitoring()
        {
            if (!_isMonitoring) return;
            
            lock (_lockObject)
            {
                if (!_isMonitoring) return;
                
                try
                {
                    AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
                    AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                    AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= OnReflectionOnlyAssemblyResolve;
                    
                    _isMonitoring = false;
                    
                    LogEvent("Assembly loading monitoring stopped", AssemblyLoadEventType.MonitoringStopped);
                    
                    // Generate summary report
                    GenerateSummaryReport();
                }
                catch (Exception ex)
                {
                    LogEvent($"Error stopping assembly monitoring: {ex.Message}", AssemblyLoadEventType.MonitoringError);
                }
            }
        }
        
        private Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var startTime = DateTime.Now;
            
            try
            {
                LogEvent($"Resolving assembly: {args.Name}", AssemblyLoadEventType.ResolutionAttempt, args.Name);
                
                // Try common resolution strategies
                var assembly = TryResolveAssembly(args);
                
                if (assembly != null)
                {
                    var duration = DateTime.Now - startTime;
                    LogEvent($"Successfully resolved: {args.Name} -> {assembly.Location} (took {duration.TotalMilliseconds:F1}ms)", 
                        AssemblyLoadEventType.ResolutionSuccess, args.Name);
                    return assembly;
                }
                else
                {
                    var duration = DateTime.Now - startTime;
                    var failure = new AssemblyLoadFailure
                    {
                        AssemblyName = args.Name,
                        RequestingAssembly = args.RequestingAssembly?.FullName ?? "<unknown>",
                        Timestamp = startTime,
                        Duration = duration,
                        ErrorMessage = "Assembly could not be resolved"
                    };
                    
                    _failures.TryAdd(args.Name, failure);
                    
                    LogEvent($"Failed to resolve: {args.Name} (took {duration.TotalMilliseconds:F1}ms)", 
                        AssemblyLoadEventType.ResolutionFailure, args.Name);
                }
            }
            catch (Exception ex)
            {
                var duration = DateTime.Now - startTime;
                var failure = new AssemblyLoadFailure
                {
                    AssemblyName = args.Name,
                    RequestingAssembly = args.RequestingAssembly?.FullName ?? "<unknown>",
                    Timestamp = startTime,
                    Duration = duration,
                    ErrorMessage = ex.Message,
                    Exception = ex
                };
                
                _failures.TryAdd(args.Name, failure);
                
                LogEvent($"Exception resolving {args.Name}: {ex.Message} (took {duration.TotalMilliseconds:F1}ms)", 
                    AssemblyLoadEventType.ResolutionException, args.Name);
            }
            
            return null;
        }
        
        private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try
            {
                var assembly = args.LoadedAssembly;
                var location = string.IsNullOrEmpty(assembly.Location) ? "<dynamic>" : assembly.Location;
                
                LogEvent($"Assembly loaded: {assembly.FullName} from {location}", 
                    AssemblyLoadEventType.AssemblyLoaded, assembly.FullName);
            }
            catch (Exception ex)
            {
                LogEvent($"Error logging assembly load: {ex.Message}", AssemblyLoadEventType.MonitoringError);
            }
        }
        
        private Assembly OnReflectionOnlyAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                LogEvent($"Reflection-only resolve: {args.Name}", AssemblyLoadEventType.ReflectionOnlyResolve, args.Name);
                
                // For reflection-only loads, we typically don't need to resolve
                return null;
            }
            catch (Exception ex)
            {
                LogEvent($"Error in reflection-only resolve: {ex.Message}", AssemblyLoadEventType.MonitoringError);
                return null;
            }
        }
        
        private Assembly TryResolveAssembly(ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);
            
            // Strategy 1: Try loading from the same directory as the requesting assembly
            if (args.RequestingAssembly != null && !string.IsNullOrEmpty(args.RequestingAssembly.Location))
            {
                try
                {
                    var requestingDir = Path.GetDirectoryName(args.RequestingAssembly.Location);
                    var assemblyPath = Path.Combine(requestingDir, assemblyName.Name + ".dll");
                    
                    if (File.Exists(assemblyPath))
                    {
                        LogEvent($"Found assembly at: {assemblyPath}", AssemblyLoadEventType.ResolutionStrategy);
                        return Assembly.LoadFrom(assemblyPath);
                    }
                }
                catch (Exception ex)
                {
                    LogEvent($"Strategy 1 failed: {ex.Message}", AssemblyLoadEventType.ResolutionStrategy);
                }
            }
            
            // Strategy 2: Try loading from the application base directory
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var assemblyPath = Path.Combine(baseDir, assemblyName.Name + ".dll");
                
                if (File.Exists(assemblyPath))
                {
                    LogEvent($"Found assembly in base directory: {assemblyPath}", AssemblyLoadEventType.ResolutionStrategy);
                    return Assembly.LoadFrom(assemblyPath);
                }
            }
            catch (Exception ex)
            {
                LogEvent($"Strategy 2 failed: {ex.Message}", AssemblyLoadEventType.ResolutionStrategy);
            }
            
            // Strategy 3: Try common subdirectories
            var commonSubdirs = new[] { "bin", "lib", "libs", "assemblies", "plugins" };
            
            foreach (var subdir in commonSubdirs)
            {
                try
                {
                    var subdirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, subdir);
                    if (Directory.Exists(subdirPath))
                    {
                        var assemblyPath = Path.Combine(subdirPath, assemblyName.Name + ".dll");
                        
                        if (File.Exists(assemblyPath))
                        {
                            LogEvent($"Found assembly in {subdir}: {assemblyPath}", AssemblyLoadEventType.ResolutionStrategy);
                            return Assembly.LoadFrom(assemblyPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogEvent($"Strategy 3 ({subdir}) failed: {ex.Message}", AssemblyLoadEventType.ResolutionStrategy);
                }
            }
            
            // Strategy 4: Try GAC or already loaded assemblies
            try
            {
                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                var matchingAssembly = loadedAssemblies.FirstOrDefault(a => 
                    string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
                
                if (matchingAssembly != null)
                {
                    LogEvent($"Found matching loaded assembly: {matchingAssembly.FullName}", AssemblyLoadEventType.ResolutionStrategy);
                    return matchingAssembly;
                }
            }
            catch (Exception ex)
            {
                LogEvent($"Strategy 4 failed: {ex.Message}", AssemblyLoadEventType.ResolutionStrategy);
            }
            
            return null;
        }
        
        private void LogCurrentlyLoadedAssemblies()
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name).ToList();
                
                LogEvent($"Currently loaded assemblies ({assemblies.Count}):", AssemblyLoadEventType.InitialState);
                
                foreach (var assembly in assemblies)
                {
                    try
                    {
                        var name = assembly.GetName();
                        var location = string.IsNullOrEmpty(assembly.Location) ? "<dynamic>" : assembly.Location;
                        LogEvent($"  {name.Name} v{name.Version} - {location}", AssemblyLoadEventType.InitialState);
                    }
                    catch (Exception ex)
                    {
                        LogEvent($"  <Error getting assembly info: {ex.Message}>", AssemblyLoadEventType.InitialState);
                    }
                }
            }
            catch (Exception ex)
            {
                LogEvent($"Failed to log currently loaded assemblies: {ex.Message}", AssemblyLoadEventType.MonitoringError);
            }
        }
        
        private void LogEvent(string message, AssemblyLoadEventType eventType, string assemblyName = null)
        {
            var loadEvent = new AssemblyLoadEvent
            {
                Timestamp = DateTime.Now,
                EventType = eventType,
                Message = message,
                AssemblyName = assemblyName,
                ThreadId = Thread.CurrentThread.ManagedThreadId
            };
            
            _loadEvents.Enqueue(loadEvent);
            
            // Also write to file immediately for critical events
            if (eventType == AssemblyLoadEventType.ResolutionFailure || 
                eventType == AssemblyLoadEventType.ResolutionException ||
                eventType == AssemblyLoadEventType.MonitoringError)
            {
                WriteEventToFile(loadEvent);
            }
            
            // Write to debug output for immediate visibility
            Debug.WriteLine($"[ASSEMBLY] {eventType}: {message}");
        }
        
        private void WriteEventToFile(AssemblyLoadEvent loadEvent)
        {
            try
            {
                var logEntry = $"[{loadEvent.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{loadEvent.ThreadId:D3}] {loadEvent.EventType}: {loadEvent.Message}";
                
                lock (_lockObject)
                {
                    File.AppendAllText(_logPath, logEntry + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Ignore logging failures to prevent recursive issues
            }
        }
        
        private void GenerateSummaryReport()
        {
            try
            {
                var summaryPath = Path.ChangeExtension(_logPath, ".summary.log");
                var report = new StringBuilder();
                
                report.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                report.AppendLine("                        ASSEMBLY LOADING SUMMARY REPORT");
                report.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                report.AppendLine();
                
                report.AppendLine($"Monitoring Session: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                report.AppendLine($"Total Events: {_loadEvents.Count}");
                report.AppendLine($"Total Failures: {_failures.Count}");
                report.AppendLine();
                
                // Event type summary
                var eventsByType = _loadEvents.GroupBy(e => e.EventType)
                    .ToDictionary(g => g.Key, g => g.Count());
                
                report.AppendLine("=== EVENT SUMMARY ===");
                foreach (var kvp in eventsByType.OrderBy(kvp => kvp.Key.ToString()))
                {
                    report.AppendLine($"{kvp.Key}: {kvp.Value}");
                }
                report.AppendLine();
                
                // Failure details
                if (_failures.Any())
                {
                    report.AppendLine("=== ASSEMBLY LOAD FAILURES ===");
                    foreach (var failure in _failures.Values.OrderBy(f => f.Timestamp))
                    {
                        report.AppendLine($"Assembly: {failure.AssemblyName}");
                        report.AppendLine($"Requested by: {failure.RequestingAssembly}");
                        report.AppendLine($"Time: {failure.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
                        report.AppendLine($"Duration: {failure.Duration.TotalMilliseconds:F1}ms");
                        report.AppendLine($"Error: {failure.ErrorMessage}");
                        
                        if (failure.Exception != null)
                        {
                            report.AppendLine($"Exception: {failure.Exception.GetType().Name}: {failure.Exception.Message}");
                        }
                        
                        report.AppendLine();
                    }
                }
                
                // Write all events to the summary
                report.AppendLine("=== DETAILED EVENT LOG ===");
                foreach (var loadEvent in _loadEvents.OrderBy(e => e.Timestamp))
                {
                    report.AppendLine($"[{loadEvent.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{loadEvent.ThreadId:D3}] {loadEvent.EventType}: {loadEvent.Message}");
                }
                
                report.AppendLine();
                report.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                report.AppendLine("                           END OF SUMMARY REPORT");
                report.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                
                File.WriteAllText(summaryPath, report.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogEvent($"Failed to generate summary report: {ex.Message}", AssemblyLoadEventType.MonitoringError);
            }
        }
        
        public bool HasFailures => _failures.Any();
        
        public IReadOnlyCollection<AssemblyLoadFailure> GetFailures() => _failures.Values.ToList();
        
        public void ReportToGlobalHandler()
        {
            if (!HasFailures) return;
            
            try
            {
                var failureReport = new StringBuilder();
                failureReport.AppendLine($"Assembly loading detected {_failures.Count} failure(s):");
                
                foreach (var failure in _failures.Values.Take(5)) // Limit to first 5 failures
                {
                    failureReport.AppendLine($"- {failure.AssemblyName}: {failure.ErrorMessage}");
                }
                
                if (_failures.Count > 5)
                {
                    failureReport.AppendLine($"... and {_failures.Count - 5} more failures");
                }
                
                var assemblyException = new FileLoadException(
                    $"Critical assembly loading failures detected. {failureReport}",
                    _failures.First().Value.AssemblyName);
                
                CrashLoggingService.Instance.LogCrash(
                    assemblyException, 
                    "AssemblyLoadingMonitor", 
                    $"Detected {_failures.Count} assembly loading failures during startup",
                    false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to report assembly failures to global handler: {ex.Message}");
            }
        }
    }
    
    public enum AssemblyLoadEventType
    {
        MonitoringStarted,
        MonitoringStopped,
        MonitoringError,
        InitialState,
        AssemblyLoaded,
        ResolutionAttempt,
        ResolutionSuccess,
        ResolutionFailure,
        ResolutionException,
        ResolutionStrategy,
        ReflectionOnlyResolve
    }
    
    public class AssemblyLoadEvent
    {
        public DateTime Timestamp { get; set; }
        public AssemblyLoadEventType EventType { get; set; }
        public string Message { get; set; }
        public string AssemblyName { get; set; }
        public int ThreadId { get; set; }
    }
    
    public class AssemblyLoadFailure
    {
        public string AssemblyName { get; set; }
        public string RequestingAssembly { get; set; }
        public DateTime Timestamp { get; set; }
        public TimeSpan Duration { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
    }
}
