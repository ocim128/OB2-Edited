using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace OpenBullet2.Native.Infrastructure.Diagnostics
{
    /// <summary>
    /// Defines the severity levels for diagnostic logging
    /// </summary>
    public enum LogSeverity
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }

    /// <summary>
    /// Configuration for crash logging behavior
    /// </summary>
    public class CrashLoggingConfig
    {
        public LogSeverity MinimumLogLevel { get; set; } = LogSeverity.Error;
        public long MaxLogFileSizeBytes { get; set; } = 5 * 1024 * 1024; // 5MB
        public int MaxLogFiles { get; set; } = 10;
        public int LogRetentionDays { get; set; } = 30;
        public bool EnableCompression { get; set; } = true;
        public bool IncludeSystemInfo { get; set; } = true;
        public bool IncludeEnvironmentVars { get; set; } = false;
        public bool IncludeLoadedAssemblies { get; set; } = false;
    }

    /// <summary>
    /// Optimized crash logging service with severity-based filtering, log rotation,
    /// and compression to minimize disk space usage while maintaining actionable information.
    /// </summary>
    public sealed class CrashLoggingService
    {
        private readonly string _sessionId;
        private readonly string _crashDir;
        private readonly string _systemInfoCache;
        private readonly object _lockObject = new object();
        private readonly CrashLoggingConfig _config;
        private static readonly Lazy<CrashLoggingService> _instance = new Lazy<CrashLoggingService>(() => new CrashLoggingService());
        
        public static CrashLoggingService Instance => _instance.Value;
        
        private CrashLoggingService()
        {
            _sessionId = Guid.NewGuid().ToString("N")[..12];
            _config = new CrashLoggingConfig();
            
            try
            {
                var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var userDataPath = Path.Combine(appDirectory, "UserData");
                var logsRoot = Path.Combine(userDataPath, "Logs");
                _crashDir = Path.Combine(logsRoot, "Crashes");
                Directory.CreateDirectory(_crashDir);
                
                // Clean up old logs on startup
                CleanupOldLogs();
            }
            catch
            {
                _crashDir = AppDomain.CurrentDomain.BaseDirectory;
            }
            
            // Pre-cache system information only if enabled to reduce memory usage
            _systemInfoCache = _config.IncludeSystemInfo ? CacheSystemInformation() : string.Empty;
        }
        
        public void LogCrash(Exception exception, string source, string context = null, bool isTerminating = false)
        {
            LogCrash(exception, source, context, isTerminating, DetermineSeverity(exception, isTerminating));
        }
        
        public void LogCrash(Exception exception, string source, string context, bool isTerminating, LogSeverity severity)
        {
            if (exception == null || severity < _config.MinimumLogLevel) return;
            
            lock (_lockObject)
            {
                try
                {
                    var crashId = Guid.NewGuid().ToString("N")[..8];
                    var timestamp = DateTime.Now;
                    var fileName = $"crash-{severity.ToString().ToLower()}-{timestamp:yyyyMMdd-HHmmss}-{crashId}.log";
                    var filePath = Path.Combine(_crashDir, fileName);
                    
                    // Check if we need to rotate logs before writing
                    RotateLogsIfNeeded();
                    
                    var crashReport = BuildCrashReport(exception, source, context, isTerminating, crashId, timestamp, severity);
                    
                    // Write compressed or uncompressed based on config
                    if (_config.EnableCompression && severity < LogSeverity.Critical)
                    {
                        WriteCompressedLog(filePath + ".gz", crashReport);
                    }
                    else
                    {
                        File.WriteAllText(filePath, crashReport, Encoding.UTF8);
                    }
                    
                    // Only write latest crash file for critical/error level issues
                    if (severity >= LogSeverity.Error)
                    {
                        var latestPath = Path.Combine(_crashDir, "latest-crash.log");
                        File.WriteAllText(latestPath, crashReport, Encoding.UTF8);
                    }
                    
                    // Log to debug output for immediate visibility
                    Debug.WriteLine($"CRASH LOGGED [{severity}]: {crashId} - {exception.GetType().Name}: {exception.Message}");
                    
                    // Try to generate crash dump only for severe crashes
                    if (isTerminating || severity >= LogSeverity.Critical || IsSevereException(exception))
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
        
        private string BuildCrashReport(Exception exception, string source, string context, bool isTerminating, string crashId, DateTime timestamp, LogSeverity severity)
        {
            var report = new StringBuilder();
            
            // Compact header for space efficiency
            report.AppendLine($"=== OPENBULLET2 CRASH REPORT [{severity}] ===");
            
            // Essential crash metadata only
            report.AppendLine($"ID: {crashId} | Session: {_sessionId} | Time: {timestamp:yyyy-MM-dd HH:mm:ss.fff}");
            report.AppendLine($"Source: {source} | Context: {context ?? "<none>"} | Terminating: {isTerminating}");
            report.AppendLine($"Thread: {Thread.CurrentThread.ManagedThreadId} ({Thread.CurrentThread.Name ?? "unnamed"})");
            report.AppendLine();
            
            // System information only if enabled and for higher severity
            if (_config.IncludeSystemInfo && severity >= LogSeverity.Error)
            {
                report.AppendLine(_systemInfoCache);
            }
            
            // Application state for critical issues only
            if (severity >= LogSeverity.Critical)
            {
                AppendApplicationState(report);
                AppendMemoryInformation(report);
            }
            
            // Exception details (always included as this is essential)
            AppendExceptionDetails(report, exception);
            
            // Optional detailed information based on config and severity
            if (_config.IncludeLoadedAssemblies && severity >= LogSeverity.Critical)
            {
                AppendLoadedAssemblies(report);
            }
            
            if (_config.IncludeEnvironmentVars && severity >= LogSeverity.Critical)
            {
                AppendEnvironmentVariables(report);
            }
            
            // Recent log entries only for critical issues
            if (severity >= LogSeverity.Critical)
            {
                AppendRecentLogEntries(report);
            }
            
            report.AppendLine($"=== END CRASH REPORT ({crashId}) ===");
            
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
                var maxExceptions = Math.Min(exceptions.Count, 3); // Limit to 3 exceptions to save space
                
                for (int i = 0; i < maxExceptions; i++)
                {
                    var (ex, depth) = exceptions[i];
                    var prefix = depth == 0 ? "PRIMARY" : $"INNER[{depth}]";
                    
                    report.AppendLine($"--- {prefix} EXCEPTION ---");
                    report.AppendLine($"Type: {ex.GetType().Name}"); // Use Name instead of FullName for brevity
                    report.AppendLine($"Message: {ex.Message}");
                    
                    if (!string.IsNullOrEmpty(ex.Source))
                        report.AppendLine($"Source: {ex.Source}");
                    
                    report.AppendLine($"HResult: 0x{ex.HResult:X8}");
                    
                    // Only include exception data if it's small
                    if (ex.Data != null && ex.Data.Count > 0 && ex.Data.Count <= 5)
                    {
                        report.AppendLine("Data:");
                        foreach (var key in ex.Data.Keys)
                        {
                            try
                            {
                                var value = ex.Data[key]?.ToString();
                                if (value?.Length <= 100) // Limit data value length
                                    report.AppendLine($"  {key}: {value}");
                            }
                            catch
                            {
                                report.AppendLine($"  {key}: <failed to serialize>");
                            }
                        }
                    }
                    
                    // Only include stack trace for primary exception or if very short
                    if (!string.IsNullOrEmpty(ex.StackTrace) && (depth == 0 || ex.StackTrace.Length < 500))
                    {
                        report.AppendLine("Stack Trace:");
                        // Truncate very long stack traces
                        if (ex.StackTrace.Length > 2000)
                        {
                            report.AppendLine(ex.StackTrace.Substring(0, 2000));
                            report.AppendLine("... (truncated)");
                        }
                        else
                        {
                            report.AppendLine(ex.StackTrace);
                        }
                    }
                    else if (string.IsNullOrEmpty(ex.StackTrace) && depth == 0)
                    {
                        report.AppendLine("Stack Trace: <no stack trace available>");
                    }
                    
                    report.AppendLine();
                }
                
                if (exceptions.Count > maxExceptions)
                {
                    report.AppendLine($"... and {exceptions.Count - maxExceptions} more inner exceptions (truncated)");
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
                    .Where(a => !a.IsDynamic) // Skip dynamic assemblies to reduce noise
                    .OrderBy(a => a.GetName().Name)
                    .Take(50) // Limit to first 50 assemblies
                    .ToArray();
                
                report.AppendLine($"Showing {assemblies.Length} assemblies (non-dynamic, limited)");
                report.AppendLine();
                
                foreach (var assembly in assemblies)
                {
                    try
                    {
                        var name = assembly.GetName();
                        var location = "<unknown>";
                        
                        try
                        {
                            location = Path.GetFileName(assembly.Location); // Just filename, not full path
                        }
                        catch
                        {
                            // Ignore location access errors
                        }
                        
                        // Compact format: Name Version [Location]
                        report.AppendLine($"{name.Name} {name.Version} [{location}]");
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"<Error: {ex.Message}>");
                    }
                }
                
                var totalAssemblies = AppDomain.CurrentDomain.GetAssemblies().Length;
                if (totalAssemblies > assemblies.Length)
                {
                    report.AppendLine($"... and {totalAssemblies - assemblies.Length} more assemblies (truncated)");
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
                
                var sensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "PASSWORD", "PWD", "SECRET", "KEY", "TOKEN", "AUTH", "CREDENTIAL",
                    "API_KEY", "PRIVATE_KEY", "CONNECTION_STRING", "CONNECTIONSTRING"
                };
                
                // Filter to only relevant environment variables
                var relevantKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "PATH", "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "NUMBER_OF_PROCESSORS",
                    "OS", "COMPUTERNAME", "USERNAME", "USERDOMAIN", "USERPROFILE",
                    "TEMP", "TMP", "WINDIR", "SYSTEMROOT", "PROGRAMFILES", "PROGRAMDATA",
                    "DOTNET_ROOT", "ASPNETCORE_ENVIRONMENT", "ENVIRONMENT"
                };
                
                var envVars = Environment.GetEnvironmentVariables()
                    .Cast<System.Collections.DictionaryEntry>()
                    .Where(kvp => relevantKeys.Contains(kvp.Key.ToString()) || 
                                 kvp.Key.ToString().StartsWith("OPENBULLET", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(kvp => kvp.Key.ToString())
                    .Take(20) // Limit to 20 most relevant variables
                    .ToArray();
                
                foreach (var envVar in envVars)
                {
                    try
                    {
                        var key = envVar.Key.ToString();
                        var value = envVar.Value?.ToString() ?? "<null>";
                        
                        // Mask sensitive values
                        if (sensitiveKeys.Any(sensitive => key.Contains(sensitive, StringComparison.OrdinalIgnoreCase)))
                        {
                            value = "<masked>";
                        }
                        else if (value.Length > 100) // Limit value length
                        {
                            value = value.Substring(0, 100) + "...";
                        }
                        
                        report.AppendLine($"{key}={value}");
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"<Error: {ex.Message}>");
                    }
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
        
        private void CleanupOldLogs()
        {
            try
            {
                if (!Directory.Exists(_crashDir))
                    return;

                var cutoffDate = DateTime.Now.AddDays(-_config.LogRetentionDays);
                var files = Directory.GetFiles(_crashDir, "*.log")
                    .Concat(Directory.GetFiles(_crashDir, "*.gz"))
                    .Where(f => File.GetCreationTime(f) < cutoffDate)
                    .ToArray();

                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                        Debug.WriteLine($"Deleted old log: {Path.GetFileName(file)}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to delete old log {Path.GetFileName(file)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during log cleanup: {ex.Message}");
            }
        }

        private void RotateLogsIfNeeded()
        {
            try
            {
                if (!Directory.Exists(_crashDir))
                    return;

                var logFiles = Directory.GetFiles(_crashDir, "*.log")
                    .OrderBy(f => File.GetCreationTime(f))
                    .ToArray();

                if (logFiles.Length >= _config.MaxLogFiles)
                {
                    var filesToDelete = logFiles.Take(logFiles.Length - _config.MaxLogFiles + 1);
                    foreach (var file in filesToDelete)
                    {
                        try
                        {
                            File.Delete(file);
                            Debug.WriteLine($"Rotated out log file: {Path.GetFileName(file)}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to rotate log file {Path.GetFileName(file)}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during log rotation: {ex.Message}");
            }
        }

        private void WriteCompressedLog(string filePath, string content)
        {
            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                using (var gzipStream = new GZipStream(fileStream, CompressionMode.Compress))
                using (var writer = new StreamWriter(gzipStream, Encoding.UTF8))
                {
                    writer.Write(content);
                }
                Debug.WriteLine($"Compressed log written: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                // Fallback to uncompressed if compression fails
                var uncompressedPath = filePath.Replace(".gz", "");
                File.WriteAllText(uncompressedPath, content, Encoding.UTF8);
                Debug.WriteLine($"Compression failed, wrote uncompressed: {ex.Message}");
            }
        }

        private LogSeverity DetermineSeverity(Exception exception, bool isTerminating)
        {
            if (isTerminating || IsSevereException(exception))
                return LogSeverity.Critical;
            
            return exception switch
            {
                ArgumentException => LogSeverity.Warning,
                InvalidOperationException => LogSeverity.Error,
                NotSupportedException => LogSeverity.Warning,
                NotImplementedException => LogSeverity.Warning,
                _ => LogSeverity.Error
            };
        }

        public void CleanupOldCrashLogs(int maxDays = 30)
        {
            CleanupOldLogs();
        }
    }
}
