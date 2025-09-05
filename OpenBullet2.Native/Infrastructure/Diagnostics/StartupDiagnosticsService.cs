using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenBullet2.Native.Infrastructure.Diagnostics
{
    /// <summary>
    /// Provides comprehensive startup diagnostics with checkpoint logging and resource verification
    /// to detect and log startup issues before they cause silent crashes.
    /// </summary>
    public sealed class StartupDiagnosticsService
    {
        private static readonly Lazy<StartupDiagnosticsService> _instance = new Lazy<StartupDiagnosticsService>(() => 
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var userDataPath = Path.Combine(appDirectory, "UserData");
            var logsRoot = Path.Combine(userDataPath, "Logs");
            Directory.CreateDirectory(logsRoot);
            return new StartupDiagnosticsService(logsRoot);
        });
        
        public static StartupDiagnosticsService Instance => _instance.Value;
        
        private readonly string _sessionId;
        private readonly string _logPath;
        private readonly StringBuilder _diagnosticsLog;
        private readonly Stopwatch _startupTimer;
        private readonly List<string> _checkpoints;
        private bool _isInitialized;

        public StartupDiagnosticsService(string logsRoot)
        {
            _sessionId = Guid.NewGuid().ToString("N")[..8];
            _startupTimer = Stopwatch.StartNew();
            _diagnosticsLog = new StringBuilder();
            _checkpoints = new List<string>();
            
            try
            {
                var diagnosticsDir = Path.Combine(logsRoot, "Startup");
                Directory.CreateDirectory(diagnosticsDir);
                _logPath = Path.Combine(diagnosticsDir, $"startup-{DateTime.Now:yyyyMMdd-HHmmss}-{_sessionId}.log");
            }
            catch
            {
                _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"startup-fallback-{_sessionId}.log");
            }

            LogCheckpoint("StartupDiagnosticsService initialized");
        }

        public void Initialize()
        {
            if (_isInitialized) return;
            
            LogCheckpoint("Starting comprehensive startup diagnostics");
            LogSystemInformation();
            LogApplicationInformation();
            VerifyEssentialResources();
            
            _isInitialized = true;
            LogCheckpoint("StartupDiagnosticsService fully initialized");
        }

        public void LogCheckpoint(string message)
        {
            var timestamp = _startupTimer.ElapsedMilliseconds;
            var logEntry = $"[{timestamp:D6}ms] [{DateTime.Now:HH:mm:ss.fff}] {message}";
            
            _checkpoints.Add(logEntry);
            _diagnosticsLog.AppendLine(logEntry);
            
            // Also write to debug output for immediate visibility
            Debug.WriteLine($"STARTUP: {logEntry}");
            
            // Flush to file periodically for crash recovery
            if (_checkpoints.Count % 5 == 0)
            {
                FlushToFile();
            }
        }
        
        public void LogCheckpoint(string context, string message)
        {
            LogCheckpoint($"[{context}] {message}");
        }

        public void LogServiceInitialization(string serviceName, bool success, Exception exception = null)
        {
            var status = success ? "SUCCESS" : "FAILED";
            var message = $"Service [{serviceName}] initialization: {status}";
            
            if (!success && exception != null)
            {
                message += $" - {exception.GetType().Name}: {exception.Message}";
            }
            
            LogCheckpoint(message);
            
            if (!success)
            {
                LogException(exception, $"Service initialization failure: {serviceName}");
            }
        }

        public void LogResourceVerification(string resourceType, string resourcePath, bool exists)
        {
            var status = exists ? "FOUND" : "MISSING";
            LogCheckpoint($"Resource [{resourceType}] at '{resourcePath}': {status}");
        }

        public void LogException(Exception exception, string context = null)
        {
            if (exception == null) return;
            
            var contextInfo = string.IsNullOrEmpty(context) ? "" : $" Context: {context}";
            LogCheckpoint($"EXCEPTION: {exception.GetType().Name}: {exception.Message}.{contextInfo}");
            
            _diagnosticsLog.AppendLine("--- Exception Details ---");
            _diagnosticsLog.AppendLine($"Type: {exception.GetType().FullName}");
            _diagnosticsLog.AppendLine($"Message: {exception.Message}");
            _diagnosticsLog.AppendLine($"Source: {exception.Source}");
            _diagnosticsLog.AppendLine($"Stack Trace:");
            _diagnosticsLog.AppendLine(exception.StackTrace ?? "<no stack trace>");
            
            if (exception.InnerException != null)
            {
                _diagnosticsLog.AppendLine("--- Inner Exception ---");
                LogExceptionRecursive(exception.InnerException, 1);
            }
            
            _diagnosticsLog.AppendLine("--- End Exception Details ---");
            _diagnosticsLog.AppendLine();
        }

        private void LogExceptionRecursive(Exception exception, int depth)
        {
            if (exception == null || depth > 10) return;
            
            var indent = new string(' ', depth * 2);
            _diagnosticsLog.AppendLine($"{indent}Type: {exception.GetType().FullName}");
            _diagnosticsLog.AppendLine($"{indent}Message: {exception.Message}");
            _diagnosticsLog.AppendLine($"{indent}Stack Trace:");
            _diagnosticsLog.AppendLine($"{indent}{exception.StackTrace?.Replace("\n", $"\n{indent}") ?? "<no stack trace>"}");
            
            if (exception.InnerException != null)
            {
                LogExceptionRecursive(exception.InnerException, depth + 1);
            }
        }

        public void LogSystemInformation()
        {
            try
            {
                _diagnosticsLog.AppendLine("=== SYSTEM INFORMATION ===");
                _diagnosticsLog.AppendLine($"Session ID: {_sessionId}");
                _diagnosticsLog.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ({DateTime.Now:o})");
                _diagnosticsLog.AppendLine($"Machine Name: {Environment.MachineName}");
                _diagnosticsLog.AppendLine($"User Name: {Environment.UserName}");
                _diagnosticsLog.AppendLine($"OS Version: {Environment.OSVersion}");
                _diagnosticsLog.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
                _diagnosticsLog.AppendLine($"64-bit Process: {Environment.Is64BitProcess}");
                _diagnosticsLog.AppendLine($".NET Version: {Environment.Version}");
                _diagnosticsLog.AppendLine($"CLR Version: {Environment.Version}");
                _diagnosticsLog.AppendLine($"Working Set: {Environment.WorkingSet:N0} bytes");
                _diagnosticsLog.AppendLine($"Processor Count: {Environment.ProcessorCount}");
                _diagnosticsLog.AppendLine($"System Directory: {Environment.SystemDirectory}");
                _diagnosticsLog.AppendLine($"Current Directory: {Environment.CurrentDirectory}");
                _diagnosticsLog.AppendLine($"Command Line: {Environment.CommandLine}");
                _diagnosticsLog.AppendLine($"Culture: {CultureInfo.CurrentCulture.Name}");
                _diagnosticsLog.AppendLine($"UI Culture: {CultureInfo.CurrentUICulture.Name}");
                _diagnosticsLog.AppendLine();
            }
            catch (Exception ex)
            {
                _diagnosticsLog.AppendLine($"Failed to log system information: {ex.Message}");
            }
        }

        private void LogApplicationInformation()
        {
            try
            {
                _diagnosticsLog.AppendLine("=== APPLICATION INFORMATION ===");
                
                var process = Process.GetCurrentProcess();
                _diagnosticsLog.AppendLine($"Process ID: {process.Id}");
                _diagnosticsLog.AppendLine($"Process Name: {process.ProcessName}");
                _diagnosticsLog.AppendLine($"Start Time: {process.StartTime:yyyy-MM-dd HH:mm:ss.fff}");
                
                var domain = AppDomain.CurrentDomain;
                _diagnosticsLog.AppendLine($"AppDomain: {domain.FriendlyName}");
                _diagnosticsLog.AppendLine($"Base Directory: {domain.BaseDirectory}");
                
                // Check for common config files
                var configFiles = new[] { "appsettings.json", "app.config", "web.config" };
                foreach (var configFile in configFiles)
                {
                    var configPath = Path.Combine(domain.BaseDirectory, configFile);
                    if (File.Exists(configPath))
                    {
                        _diagnosticsLog.AppendLine($"Configuration File Found: {configFile}");
                    }
                }
                
                var entryAssembly = Assembly.GetEntryAssembly();
                if (entryAssembly != null)
                {
                    _diagnosticsLog.AppendLine($"Entry Assembly: {entryAssembly.FullName}");
                    _diagnosticsLog.AppendLine($"Entry Assembly Location: {entryAssembly.Location}");
                    
                    var version = entryAssembly.GetName().Version;
                    _diagnosticsLog.AppendLine($"Application Version: {version}");
                }
                
                _diagnosticsLog.AppendLine($"Loaded Assemblies Count: {domain.GetAssemblies().Length}");
                _diagnosticsLog.AppendLine();
                
                LogCheckpoint("Application information logged");
            }
            catch (Exception ex)
            {
                _diagnosticsLog.AppendLine($"Failed to log application information: {ex.Message}");
                LogCheckpoint($"Failed to log application information: {ex.Message}");
            }
        }

        private void VerifyEssentialResources()
        {
            try
            {
                _diagnosticsLog.AppendLine("=== RESOURCE VERIFICATION ===");
                
                // Verify essential directories
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                LogResourceVerification("Base Directory", baseDir, Directory.Exists(baseDir));
                
                var userDataDir = Path.Combine(baseDir, "UserData");
                LogResourceVerification("UserData Directory", userDataDir, Directory.Exists(userDataDir));
                
                // Verify essential files
                var configFile = Path.Combine(baseDir, "appsettings.json");
                LogResourceVerification("Configuration File", configFile, File.Exists(configFile));
                
                var userAgentsFile = Path.Combine(baseDir, "user-agents.json");
                LogResourceVerification("User Agents File", userAgentsFile, File.Exists(userAgentsFile));
                
                // Verify write permissions
                try
                {
                    var testFile = Path.Combine(userDataDir, ".write_test_" + _sessionId);
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    LogResourceVerification("UserData Write Permission", userDataDir, true);
                }
                catch
                {
                    LogResourceVerification("UserData Write Permission", userDataDir, false);
                }
                
                _diagnosticsLog.AppendLine();
                LogCheckpoint("Resource verification completed");
            }
            catch (Exception ex)
            {
                _diagnosticsLog.AppendLine($"Failed to verify resources: {ex.Message}");
                LogCheckpoint($"Resource verification failed: {ex.Message}");
            }
        }

        public void LogLoadedAssemblies()
        {
            try
            {
                _diagnosticsLog.AppendLine("=== LOADED ASSEMBLIES ===");
                
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var assembly in assemblies)
                {
                    try
                    {
                        var name = assembly.GetName();
                        _diagnosticsLog.AppendLine($"{name.Name} v{name.Version} - {assembly.Location}");
                    }
                    catch (Exception ex)
                    {
                        _diagnosticsLog.AppendLine($"<Failed to get assembly info: {ex.Message}>");
                    }
                }
                
                _diagnosticsLog.AppendLine();
                LogCheckpoint($"Logged {assemblies.Length} loaded assemblies");
            }
            catch (Exception ex)
            {
                LogCheckpoint($"Failed to log assemblies: {ex.Message}");
            }
        }

        public void FlushToFile()
        {
            try
            {
                File.WriteAllText(_logPath, _diagnosticsLog.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Silent failure - don't throw during startup
            }
        }

        public void FinalizeStartup(bool success)
        {
            var duration = _startupTimer.ElapsedMilliseconds;
            var status = success ? "SUCCESS" : "FAILED";
            
            LogCheckpoint($"Startup completed: {status} (Total time: {duration}ms)");
            
            if (success)
            {
                LogLoadedAssemblies();
            }
            
            _diagnosticsLog.AppendLine("=== STARTUP CHECKPOINTS SUMMARY ===");
            foreach (var checkpoint in _checkpoints)
            {
                _diagnosticsLog.AppendLine(checkpoint);
            }
            _diagnosticsLog.AppendLine($"=== END STARTUP LOG (Session: {_sessionId}) ===");
            
            FlushToFile();
        }
        
        public void VerifyResource(string resourceName, string resourcePath)
        {
            try
            {
                if (File.Exists(resourcePath))
                {
                    var fileInfo = new FileInfo(resourcePath);
                    LogCheckpoint($"Resource verified: {resourceName} ({fileInfo.Length} bytes)");
                }
                else
                {
                    LogCheckpoint($"Resource missing: {resourceName} at {resourcePath}");
                }
            }
            catch (Exception ex)
            {
                LogCheckpoint($"Resource verification failed for {resourceName}: {ex.Message}");
            }
        }
        
        public string GetSessionId() => _sessionId;
        public long GetElapsedMilliseconds() => _startupTimer.ElapsedMilliseconds;
        public IReadOnlyList<string> GetCheckpoints() => _checkpoints.AsReadOnly();
    }
}