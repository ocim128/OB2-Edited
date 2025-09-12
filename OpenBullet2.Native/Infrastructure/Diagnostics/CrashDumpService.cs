using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OpenBullet2.Native.Infrastructure.Diagnostics
{
    /// <summary>
    /// Provides crash dump generation capabilities for severe application failures.
    /// Generates minidumps on Windows and diagnostic information on other platforms.
    /// </summary>
    public sealed class CrashDumpService
    {
        private static readonly Lazy<CrashDumpService> _instance = new Lazy<CrashDumpService>(() => new CrashDumpService());
        public static CrashDumpService Instance => _instance.Value;
        
        private readonly string _dumpDirectory;
        private readonly object _lockObject = new object();
        
        // Windows API imports for minidump generation
        [DllImport("dbghelp.dll", SetLastError = true)]
        private static extern bool MiniDumpWriteDump(
            IntPtr hProcess,
            uint processId,
            IntPtr hFile,
            uint dumpType,
            IntPtr exceptionParam,
            IntPtr userStreamParam,
            IntPtr callbackParam);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetCurrentProcessId();
        
        private CrashDumpService()
        {
            // Create dumps directory in UserData folder
            var userDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenBullet2");
            _dumpDirectory = Path.Combine(userDataPath, "CrashDumps");
            
            try
            {
                Directory.CreateDirectory(_dumpDirectory);
                StartupDiagnosticsService.Instance?.LogCheckpoint("CrashDumpService", $"Initialized dump directory: {_dumpDirectory}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create crash dump directory: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Generates a crash dump for the current process.
        /// </summary>
        /// <param name="exception">The exception that caused the crash</param>
        /// <param name="context">Context information about the crash</param>
        /// <param name="dumpType">Type of dump to generate</param>
        /// <returns>Path to the generated dump file, or null if generation failed</returns>
        public string GenerateCrashDump(Exception exception, string context, DumpType dumpType = DumpType.MiniDump)
        {
            lock (_lockObject)
            {
                try
                {
                    var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                    var dumpFileName = $"OpenBullet2_Crash_{timestamp}_{context.Replace(" ", "_")}.dmp";
                    var dumpPath = Path.Combine(_dumpDirectory, dumpFileName);
                    
                    StartupDiagnosticsService.Instance?.LogCheckpoint("CrashDump", $"Generating crash dump: {dumpFileName}");
                    
                    bool success = false;
                    
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        success = GenerateWindowsMinidump(dumpPath, dumpType);
                    }
                    else
                    {
                        success = GenerateCrossPlatformDump(dumpPath, exception, context);
                    }
                    
                    if (success)
                    {
                        // Also generate a companion text file with crash details
                        GenerateCrashReport(dumpPath + ".txt", exception, context);
                        
                        StartupDiagnosticsService.Instance?.LogCheckpoint("CrashDump", $"Crash dump generated successfully: {dumpPath}");
                        return dumpPath;
                    }
                    else
                    {
                        StartupDiagnosticsService.Instance?.LogCheckpoint("CrashDump", "Failed to generate crash dump");
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error generating crash dump: {ex.Message}");
                    StartupDiagnosticsService.Instance?.LogCheckpoint("CrashDump", $"Error generating crash dump: {ex.Message}");
                    return null;
                }
            }
        }
        
        /// <summary>
        /// Generates a crash dump asynchronously to avoid blocking the main thread.
        /// </summary>
        public async Task<string> GenerateCrashDumpAsync(Exception exception, string context, DumpType dumpType = DumpType.MiniDump)
        {
            return await Task.Run(() => GenerateCrashDump(exception, context, dumpType));
        }
        
        /// <summary>
        /// Cleans up old crash dumps to prevent disk space issues.
        /// </summary>
        /// <param name="maxAge">Maximum age of dumps to keep</param>
        /// <param name="maxCount">Maximum number of dumps to keep</param>
        public void CleanupOldDumps(TimeSpan maxAge = default, int maxCount = 10)
        {
            try
            {
                if (maxAge == default)
                    maxAge = TimeSpan.FromDays(30); // Default to 30 days
                
                if (!Directory.Exists(_dumpDirectory))
                    return;
                
                var dumpFiles = Directory.GetFiles(_dumpDirectory, "*.dmp")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToArray();
                
                var cutoffDate = DateTime.UtcNow - maxAge;
                var filesToDelete = new List<FileInfo>();
                
                // Mark files older than maxAge for deletion
                filesToDelete.AddRange(dumpFiles.Where(f => f.CreationTime < cutoffDate));
                
                // Mark excess files for deletion (keep only maxCount newest)
                if (dumpFiles.Length > maxCount)
                {
                    filesToDelete.AddRange(dumpFiles.Skip(maxCount));
                }
                
                // Delete marked files and their companion text files
                foreach (var file in filesToDelete.Distinct())
                {
                    try
                    {
                        File.Delete(file.FullName);
                        
                        // Also delete companion text file if it exists
                        var textFile = file.FullName + ".txt";
                        if (File.Exists(textFile))
                            File.Delete(textFile);
                        
                        StartupDiagnosticsService.Instance?.LogCheckpoint("CrashDump.Cleanup", $"Deleted old dump: {file.Name}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to delete dump file {file.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during crash dump cleanup: {ex.Message}");
            }
        }
        
        private bool GenerateWindowsMinidump(string dumpPath, DumpType dumpType)
        {
            try
            {
                using (var fileStream = new FileStream(dumpPath, FileMode.Create, FileAccess.Write))
                {
                    var process = GetCurrentProcess();
                    var processId = GetCurrentProcessId();
                    
                    uint miniDumpType = dumpType switch
                    {
                        DumpType.MiniDump => 0x00000000, // MiniDumpNormal
                        DumpType.MiniDumpWithData => 0x00000002, // MiniDumpWithDataSegs
                        DumpType.FullDump => 0x00000002 | 0x00000001, // MiniDumpWithFullMemory | MiniDumpWithDataSegs
                        _ => 0x00000000
                    };
                    
                    return MiniDumpWriteDump(
                        process,
                        processId,
                        fileStream.SafeFileHandle.DangerousGetHandle(),
                        miniDumpType,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to generate Windows minidump: {ex.Message}");
                return false;
            }
        }
        
        private bool GenerateCrossPlatformDump(string dumpPath, Exception exception, string context)
        {
            try
            {
                // For non-Windows platforms, generate a detailed text dump
                var dumpContent = new StringBuilder();
                
                dumpContent.AppendLine("=== OpenBullet2 Cross-Platform Crash Dump ===");
                dumpContent.AppendLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                dumpContent.AppendLine($"Context: {context}");
                dumpContent.AppendLine($"Platform: {RuntimeInformation.OSDescription}");
                dumpContent.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}");
                dumpContent.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
                dumpContent.AppendLine();
                
                // Process information
                var process = Process.GetCurrentProcess();
                dumpContent.AppendLine("=== Process Information ===");
                dumpContent.AppendLine($"Process ID: {process.Id}");
                dumpContent.AppendLine($"Process Name: {process.ProcessName}");
                dumpContent.AppendLine($"Working Set: {process.WorkingSet64:N0} bytes");
                dumpContent.AppendLine($"Private Memory: {process.PrivateMemorySize64:N0} bytes");
                dumpContent.AppendLine($"Threads: {process.Threads.Count}");
                dumpContent.AppendLine();
                
                // Exception information
                if (exception != null)
                {
                    dumpContent.AppendLine("=== Exception Information ===");
                    dumpContent.AppendLine($"Type: {exception.GetType().FullName}");
                    dumpContent.AppendLine($"Message: {exception.Message}");
                    dumpContent.AppendLine($"Stack Trace:");
                    dumpContent.AppendLine(exception.StackTrace);
                    
                    var innerEx = exception.InnerException;
                    int innerCount = 1;
                    while (innerEx != null && innerCount <= 5)
                    {
                        dumpContent.AppendLine($"\n--- Inner Exception {innerCount} ---");
                        dumpContent.AppendLine($"Type: {innerEx.GetType().FullName}");
                        dumpContent.AppendLine($"Message: {innerEx.Message}");
                        dumpContent.AppendLine($"Stack Trace:");
                        dumpContent.AppendLine(innerEx.StackTrace);
                        
                        innerEx = innerEx.InnerException;
                        innerCount++;
                    }
                }
                
                File.WriteAllText(dumpPath, dumpContent.ToString());
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to generate cross-platform dump: {ex.Message}");
                return false;
            }
        }
        
        private void GenerateCrashReport(string reportPath, Exception exception, string context)
        {
            try
            {
                var report = new StringBuilder();
                
                report.AppendLine("=== OpenBullet2 Crash Report ===");
                report.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                report.AppendLine($"Context: {context}");
                report.AppendLine();
                
                // System information
                report.AppendLine("=== System Information ===");
                report.AppendLine($"OS: {Environment.OSVersion}");
                report.AppendLine($"Platform: {RuntimeInformation.OSDescription}");
                report.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}");
                report.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
                report.AppendLine($"Machine Name: {Environment.MachineName}");
                report.AppendLine($"User: {Environment.UserName}");
                report.AppendLine($"Working Directory: {Environment.CurrentDirectory}");
                report.AppendLine();
                
                // Application information
                report.AppendLine("=== Application Information ===");
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                report.AppendLine($"Version: {assembly.GetName().Version}");
                report.AppendLine($"Location: {assembly.Location}");
                report.AppendLine($"Command Line: {Environment.CommandLine}");
                report.AppendLine();
                
                // Exception details
                if (exception != null)
                {
                    report.AppendLine("=== Exception Details ===");
                    report.AppendLine($"Type: {exception.GetType().FullName}");
                    report.AppendLine($"Message: {exception.Message}");
                    report.AppendLine($"Source: {exception.Source}");
                    report.AppendLine($"HResult: 0x{exception.HResult:X8}");
                    report.AppendLine();
                    
                    report.AppendLine("Stack Trace:");
                    report.AppendLine(exception.StackTrace);
                    report.AppendLine();
                    
                    // Inner exceptions
                    var innerEx = exception.InnerException;
                    int innerCount = 1;
                    while (innerEx != null && innerCount <= 10)
                    {
                        report.AppendLine($"--- Inner Exception {innerCount} ---");
                        report.AppendLine($"Type: {innerEx.GetType().FullName}");
                        report.AppendLine($"Message: {innerEx.Message}");
                        report.AppendLine($"Stack Trace:");
                        report.AppendLine(innerEx.StackTrace);
                        report.AppendLine();
                        
                        innerEx = innerEx.InnerException;
                        innerCount++;
                    }
                }
                
                File.WriteAllText(reportPath, report.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to generate crash report: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Gets the directory where crash dumps are stored.
        /// </summary>
        public string DumpDirectory => _dumpDirectory;
    }
    
    /// <summary>
    /// Specifies the type of crash dump to generate.
    /// </summary>
    public enum DumpType
    {
        /// <summary>
        /// Minimal dump with basic information (smallest size).
        /// </summary>
        MiniDump,
        
        /// <summary>
        /// Mini dump with data segments (medium size).
        /// </summary>
        MiniDumpWithData,
        
        /// <summary>
        /// Full memory dump (largest size, use sparingly).
        /// </summary>
        FullDump
    }
}
