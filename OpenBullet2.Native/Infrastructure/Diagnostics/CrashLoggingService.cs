using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace OpenBullet2.Native.Infrastructure.Diagnostics
{
    /// <summary>
    /// Comprehensive crash logging service that captures detailed system information,
    /// stack traces, and application state for thorough crash analysis.
    /// </summary>
    public sealed class CrashLoggingService
    {
        private readonly string _sessionId;
        private readonly string _crashDir;
        private readonly string _systemInfoCache;
        private readonly object _lockObject = new object();
        private static readonly Lazy<CrashLoggingService> _instance = new Lazy<CrashLoggingService>(() => new CrashLoggingService());
        
        public static CrashLoggingService Instance => _instance.Value;
        
        private CrashLoggingService()
        {
            _sessionId = Guid.NewGuid().ToString("N")[..12];
            
            try
            {
                var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var userDataPath = Path.Combine(appDirectory, "UserData");
                var logsRoot = Path.Combine(userDataPath, "Logs");
                _crashDir = Path.Combine(logsRoot, "Crashes");
                Directory.CreateDirectory(_crashDir);
            }
            catch
            {
                _crashDir = AppDomain.CurrentDomain.BaseDirectory;
            }
            
            // Pre-cache system information to avoid delays during crash logging
            _systemInfoCache = CacheSystemInformation();
        }
        
        public void LogCrash(Exception exception, string source, string context = null, bool isTerminating = false)
        {
            if (exception == null) return;
            
            lock (_lockObject)
            {
                try
                {
                    var crashId = Guid.NewGuid().ToString("N")[..8];
                    var timestamp = DateTime.Now;
                    var fileName = $"crash-{timestamp:yyyyMMdd-HHmmss}-{crashId}.log";
                    var filePath = Path.Combine(_crashDir, fileName);
                    
                    var crashReport = BuildCrashReport(exception, source, context, isTerminating, crashId, timestamp);
                    
                    File.WriteAllText(filePath, crashReport, Encoding.UTF8);
                    
                    // Also write to a latest crash file for quick access
                    var latestPath = Path.Combine(_crashDir, "latest-crash.log");
                    File.WriteAllText(latestPath, crashReport, Encoding.UTF8);
                    
                    // Log to debug output for immediate visibility
                    Debug.WriteLine($"CRASH LOGGED: {crashId} - {exception.GetType().Name}: {exception.Message}");
                    
                    // Try to generate crash dump for severe crashes
                    if (isTerminating || IsSevereException(exception))
                    {
                        TryGenerateCrashDump(exception, source, context, crashId);
                    }
                }
                catch (Exception logEx)
                {
                    // Last resort - write to base directory
                    try
                    {
                        var fallbackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"crash-fallback-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                        var basicReport = $"CRASH LOGGING FAILED: {logEx.Message}\n\nOriginal Exception:\n{exception}";
                        File.WriteAllText(fallbackPath, basicReport, Encoding.UTF8);
                    }
                    catch
                    {
                        // Give up - can't log anything
                    }
                }
            }
        }
        
        private string BuildCrashReport(Exception exception, string source, string context, bool isTerminating, string crashId, DateTime timestamp)
        {
            var report = new StringBuilder();
            
            // Header
            report.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
            report.AppendLine("                           OPENBULLET2 CRASH REPORT");
            report.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
            report.AppendLine();
            
            // Crash metadata
            report.AppendLine("=== CRASH METADATA ===");
            report.AppendLine($"Crash ID: {crashId}");
            report.AppendLine($"Session ID: {_sessionId}");
            report.AppendLine($"Timestamp: {timestamp:yyyy-MM-dd HH:mm:ss.fff} ({timestamp:o})");
            report.AppendLine($"Source: {source}");
            report.AppendLine($"Context: {context ?? "<none>"}");
            report.AppendLine($"Terminating: {isTerminating}");
            report.AppendLine($"Thread ID: {Thread.CurrentThread.ManagedThreadId}");
            report.AppendLine($"Thread Name: {Thread.CurrentThread.Name ?? "<unnamed>"}");
            report.AppendLine($"Is Background Thread: {Thread.CurrentThread.IsBackground}");
            report.AppendLine($"Is Thread Pool Thread: {Thread.CurrentThread.IsThreadPoolThread}");
            report.AppendLine();
            
            // System information (cached)
            report.AppendLine(_systemInfoCache);
            
            // Current application state
            AppendApplicationState(report);
            
            // Memory information
            AppendMemoryInformation(report);
            
            // Exception details
            AppendExceptionDetails(report, exception);
            
            // Loaded assemblies
            AppendLoadedAssemblies(report);
            
            // Environment variables
            AppendEnvironmentVariables(report);
            
            // Recent log entries (if available)
            AppendRecentLogEntries(report);
            
            report.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
            report.AppendLine($"                        END OF CRASH REPORT ({crashId})");
            report.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
            
            return report.ToString();
        }
        
        private string CacheSystemInformation()
        {
            var info = new StringBuilder();
            
            try
            {
                info.AppendLine("=== SYSTEM INFORMATION ===");
                info.AppendLine($"Machine Name: {Environment.MachineName}");
                info.AppendLine($"User Name: {Environment.UserName}");
                info.AppendLine($"Domain Name: {Environment.UserDomainName}");
                info.AppendLine($"OS Version: {Environment.OSVersion}");
                info.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
                info.AppendLine($"64-bit Process: {Environment.Is64BitProcess}");
                info.AppendLine($".NET Version: {Environment.Version}");
                info.AppendLine($"Processor Count: {Environment.ProcessorCount}");
                info.AppendLine($"System Directory: {Environment.SystemDirectory}");
                info.AppendLine($"Culture: {CultureInfo.CurrentCulture.Name}");
                info.AppendLine($"UI Culture: {CultureInfo.CurrentUICulture.Name}");
                
                // Additional Windows-specific information
                try
                {
                    var osInfo = GetWindowsVersionInfo();
                    if (!string.IsNullOrEmpty(osInfo))
                    {
                        info.AppendLine($"Windows Version: {osInfo}");
                    }
                }
                catch { }
                
                // Hardware information
                try
                {
                    var memoryInfo = GetPhysicalMemoryInfo();
                    if (!string.IsNullOrEmpty(memoryInfo))
                    {
                        info.AppendLine($"Physical Memory: {memoryInfo}");
                    }
                }
                catch { }
                
                info.AppendLine();
            }
            catch (Exception ex)
            {
                info.AppendLine($"Failed to cache system information: {ex.Message}");
                info.AppendLine();
            }
            
            return info.ToString();
        }
        
        private void AppendApplicationState(StringBuilder report)
        {
            try
            {
                report.AppendLine("=== APPLICATION STATE ===");
                
                var process = Process.GetCurrentProcess();
                report.AppendLine($"Process ID: {process.Id}");
                report.AppendLine($"Process Name: {process.ProcessName}");
                report.AppendLine($"Start Time: {process.StartTime:yyyy-MM-dd HH:mm:ss.fff}");
                report.AppendLine($"Total Processor Time: {process.TotalProcessorTime}");
                report.AppendLine($"User Processor Time: {process.UserProcessorTime}");
                report.AppendLine($"Privileged Processor Time: {process.PrivilegedProcessorTime}");
                
                var domain = AppDomain.CurrentDomain;
                report.AppendLine($"AppDomain: {domain.FriendlyName}");
                report.AppendLine($"Base Directory: {domain.BaseDirectory}");
                report.AppendLine($"Shadow Copy Files: {domain.ShadowCopyFiles}");
                
                // Check for common config files
                var configFiles = new[] { "appsettings.json", "app.config", "web.config" };
                foreach (var configFile in configFiles)
                {
                    var configPath = Path.Combine(domain.BaseDirectory, configFile);
                    if (File.Exists(configPath))
                    {
                        report.AppendLine($"Configuration File Found: {configFile}");
                    }
                }
                
                var entryAssembly = Assembly.GetEntryAssembly();
                if (entryAssembly != null)
                {
                    report.AppendLine($"Entry Assembly: {entryAssembly.FullName}");
                    report.AppendLine($"Entry Assembly Location: {entryAssembly.Location}");
                }
                
                // WPF Application state
                if (Application.Current != null)
                {
                    report.AppendLine($"WPF Application: {Application.Current.GetType().Name}");
                    report.AppendLine($"Main Window: {Application.Current.MainWindow?.GetType().Name ?? "<none>"}");
                    report.AppendLine($"Shutdown Mode: {Application.Current.ShutdownMode}");
                    report.AppendLine($"Windows Count: {Application.Current.Windows.Count}");
                }
                
                report.AppendLine();
            }
            catch (Exception ex)
            {
                report.AppendLine($"Failed to get application state: {ex.Message}");
                report.AppendLine();
            }
        }
        
        private void AppendMemoryInformation(StringBuilder report)
        {
            try
            {
                report.AppendLine("=== MEMORY INFORMATION ===");
                
                var process = Process.GetCurrentProcess();
                report.AppendLine($"Working Set: {process.WorkingSet64:N0} bytes ({process.WorkingSet64 / 1024 / 1024:N1} MB)");
                report.AppendLine($"Private Memory: {process.PrivateMemorySize64:N0} bytes ({process.PrivateMemorySize64 / 1024 / 1024:N1} MB)");
                report.AppendLine($"Virtual Memory: {process.VirtualMemorySize64:N0} bytes ({process.VirtualMemorySize64 / 1024 / 1024:N1} MB)");
                report.AppendLine($"Paged Memory: {process.PagedMemorySize64:N0} bytes ({process.PagedMemorySize64 / 1024 / 1024:N1} MB)");
                report.AppendLine($"Paged System Memory: {process.PagedSystemMemorySize64:N0} bytes ({process.PagedSystemMemorySize64 / 1024 / 1024:N1} MB)");
                report.AppendLine($"Non-paged System Memory: {process.NonpagedSystemMemorySize64:N0} bytes ({process.NonpagedSystemMemorySize64 / 1024 / 1024:N1} MB)");
                
                // GC information
                report.AppendLine($"GC Total Memory: {GC.GetTotalMemory(false):N0} bytes ({GC.GetTotalMemory(false) / 1024 / 1024:N1} MB)");
                report.AppendLine($"GC Gen 0 Collections: {GC.CollectionCount(0)}");
                report.AppendLine($"GC Gen 1 Collections: {GC.CollectionCount(1)}");
                report.AppendLine($"GC Gen 2 Collections: {GC.CollectionCount(2)}");
                
                report.AppendLine();
            }
            catch (Exception ex)
            {
                report.AppendLine($"Failed to get memory information: {ex.Message}");
                report.AppendLine();
            }
        }
        
        private void AppendExceptionDetails(StringBuilder report, Exception exception)
        {
            try
            {
                report.AppendLine("=== EXCEPTION DETAILS ===");
                
                var exceptions = FlattenExceptions(exception).ToList();
                for (int i = 0; i < exceptions.Count; i++)
                {
                    var (ex, depth) = exceptions[i];
                    var prefix = depth == 0 ? "PRIMARY" : $"INNER[{depth}]";
                    
                    report.AppendLine($"--- {prefix} EXCEPTION ---");
                    report.AppendLine($"Type: {ex.GetType().FullName}");
                    report.AppendLine($"Assembly: {ex.GetType().Assembly.FullName}");
                    report.AppendLine($"Message: {ex.Message}");
                    report.AppendLine($"Source: {ex.Source ?? "<unknown>"}");
                    report.AppendLine($"HelpLink: {ex.HelpLink ?? "<none>"}");
                    report.AppendLine($"HResult: 0x{ex.HResult:X8} ({ex.HResult})");
                    
                    if (ex.Data != null && ex.Data.Count > 0)
                    {
                        report.AppendLine("Data:");
                        foreach (var key in ex.Data.Keys)
                        {
                            try
                            {
                                report.AppendLine($"  {key}: {ex.Data[key]}");
                            }
                            catch
                            {
                                report.AppendLine($"  {key}: <failed to serialize>");
                            }
                        }
                    }
                    
                    report.AppendLine("Stack Trace:");
                    if (!string.IsNullOrEmpty(ex.StackTrace))
                    {
                        report.AppendLine(ex.StackTrace);
                    }
                    else
                    {
                        report.AppendLine("<no stack trace available>");
                    }
                    
                    report.AppendLine();
                }
            }
            catch (Exception ex)
            {
                report.AppendLine($"Failed to format exception details: {ex.Message}");
                report.AppendLine($"Original exception string: {exception}");
                report.AppendLine();
            }
        }
        
        private void AppendLoadedAssemblies(StringBuilder report)
        {
            try
            {
                report.AppendLine("=== LOADED ASSEMBLIES ===");
                
                var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                    .OrderBy(a => a.GetName().Name)
                    .ToList();
                
                report.AppendLine($"Total Count: {assemblies.Count}");
                report.AppendLine();
                
                foreach (var assembly in assemblies)
                {
                    try
                    {
                        var name = assembly.GetName();
                        var location = string.IsNullOrEmpty(assembly.Location) ? "<dynamic>" : assembly.Location;
                        report.AppendLine($"{name.Name} v{name.Version} - {location}");
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"<Failed to get assembly info: {ex.Message}>");
                    }
                }
                
                report.AppendLine();
            }
            catch (Exception ex)
            {
                report.AppendLine($"Failed to get loaded assemblies: {ex.Message}");
                report.AppendLine();
            }
        }
        
        private void AppendEnvironmentVariables(StringBuilder report)
        {
            try
            {
                report.AppendLine("=== ENVIRONMENT VARIABLES ===");
                
                var variables = Environment.GetEnvironmentVariables()
                    .Cast<System.Collections.DictionaryEntry>()
                    .OrderBy(kvp => kvp.Key.ToString())
                    .ToList();
                
                foreach (var variable in variables)
                {
                    var key = variable.Key.ToString();
                    var value = variable.Value?.ToString() ?? "<null>";
                    
                    // Mask sensitive information
                    if (key.ToUpperInvariant().Contains("PASSWORD") || 
                        key.ToUpperInvariant().Contains("SECRET") ||
                        key.ToUpperInvariant().Contains("TOKEN") ||
                        key.ToUpperInvariant().Contains("KEY"))
                    {
                        value = "<masked>";
                    }
                    
                    report.AppendLine($"{key}={value}");
                }
                
                report.AppendLine();
            }
            catch (Exception ex)
            {
                report.AppendLine($"Failed to get environment variables: {ex.Message}");
                report.AppendLine();
            }
        }
        
        private void AppendRecentLogEntries(StringBuilder report)
        {
            try
            {
                report.AppendLine("=== RECENT LOG ENTRIES ===");
                
                // Try to read recent startup logs
                var startupLogsDir = Path.Combine(_crashDir, "..", "Startup");
                if (Directory.Exists(startupLogsDir))
                {
                    var recentStartupLog = Directory.GetFiles(startupLogsDir, "startup-*.log")
                        .OrderByDescending(f => File.GetCreationTime(f))
                        .FirstOrDefault();
                    
                    if (recentStartupLog != null)
                    {
                        report.AppendLine("Recent Startup Log:");
                        var lines = File.ReadAllLines(recentStartupLog).TakeLast(20);
                        foreach (var line in lines)
                        {
                            report.AppendLine($"  {line}");
                        }
                        report.AppendLine();
                    }
                }
                
                report.AppendLine("<End of recent log entries>");
                report.AppendLine();
            }
            catch (Exception ex)
            {
                report.AppendLine($"Failed to get recent log entries: {ex.Message}");
                report.AppendLine();
            }
        }
        
        private IEnumerable<(Exception ex, int depth)> FlattenExceptions(Exception exception, int depth = 0)
        {
            if (exception == null || depth > 20) yield break;
            
            yield return (exception, depth);
            
            if (exception is AggregateException aggregateEx)
            {
                foreach (var innerEx in aggregateEx.InnerExceptions)
                {
                    foreach (var item in FlattenExceptions(innerEx, depth + 1))
                        yield return item;
                }
            }
            else if (exception.InnerException != null)
            {
                foreach (var item in FlattenExceptions(exception.InnerException, depth + 1))
                    yield return item;
            }
        }
        
        private string GetWindowsVersionInfo()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key != null)
                {
                    var productName = key.GetValue("ProductName")?.ToString();
                    var displayVersion = key.GetValue("DisplayVersion")?.ToString();
                    var buildNumber = key.GetValue("CurrentBuildNumber")?.ToString();
                    
                    return $"{productName} {displayVersion} (Build {buildNumber})";
                }
            }
            catch { }
            
            return null;
        }
        
        private string GetPhysicalMemoryInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    var totalMemory = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                    return $"{totalMemory:N0} bytes ({totalMemory / 1024 / 1024 / 1024:N1} GB)";
                }
            }
            catch { }
            
            return null;
        }
        
        private void TryGenerateCrashDump(Exception exception, string source, string context, string crashId)
        {
            try
            {
                // Use the new CrashDumpService to generate proper crash dumps
                var dumpContext = $"{source}_{crashId}";
                var dumpPath = CrashDumpService.Instance.GenerateCrashDump(exception, dumpContext, DumpType.MiniDumpWithData);
                
                if (!string.IsNullOrEmpty(dumpPath))
                {
                    Debug.WriteLine($"Crash dump generated: {dumpPath}");
                }
                else
                {
                    // Fallback to basic dump info if crash dump generation fails
                    var fallbackPath = Path.Combine(_crashDir, $"crashdump-{crashId}.txt");
                    var dumpInfo = $"Crash dump generation attempted at {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n" +
                                  $"Crash ID: {crashId}\n" +
                                  $"Session ID: {_sessionId}\n" +
                                  $"Process ID: {Process.GetCurrentProcess().Id}\n" +
                                  $"Source: {source}\n" +
                                  $"Context: {context ?? "<none>"}\n" +
                                  $"Exception: {exception.GetType().Name}: {exception.Message}";
                    
                    File.WriteAllText(fallbackPath, dumpInfo, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to generate crash dump: {ex.Message}");
            }
        }
        
        private bool IsSevereException(Exception exception)
        {
            return exception switch
            {
                OutOfMemoryException => true,
                StackOverflowException => true,
                AccessViolationException => true,
                InvalidOperationException when exception.Message.Contains("cross-thread") => true,
                System.Runtime.InteropServices.SEHException => true,
                BadImageFormatException => true,
                _ => false
            };
        }
        
        public string GetSessionId() => _sessionId;
        
        public void CleanupOldCrashLogs(int maxDays = 30)
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-maxDays);
                var files = Directory.GetFiles(_crashDir, "crash-*.log")
                    .Where(f => File.GetCreationTime(f) < cutoffDate)
                    .ToList();
                
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Ignore individual file deletion failures
                    }
                }
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }
}