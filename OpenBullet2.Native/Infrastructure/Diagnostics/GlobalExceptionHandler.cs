using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace OpenBullet2.Native.Infrastructure.Diagnostics
{
    /// <summary>
    /// Enhanced centralized unhandled exception handler that integrates with comprehensive crash logging,
    /// startup diagnostics, and assembly loading monitoring. Logs AppDomain, Dispatcher, and TaskScheduler 
    /// exceptions to rolling log files under the provided Logs directory. Safe, dependency-free, and resilient.
    /// </summary>
    public sealed class GlobalExceptionHandler : IDisposable
    {
        private readonly string _logsRoot;
        private readonly string _crashDir;
        private readonly long _maxFileBytes;
        private readonly int _maxRollFiles;
        private bool _isInitialized;
        private StartupDiagnosticsService _startupDiagnostics;
        private AssemblyLoadingMonitor _assemblyMonitor;

        public GlobalExceptionHandler(string logsRoot, long maxFileBytes = 2 * 1024 * 1024, int maxRollFiles = 5)
        {
            _logsRoot = logsRoot ?? AppDomain.CurrentDomain.BaseDirectory;
            _crashDir = Path.Combine(_logsRoot, "Crashes");
            _maxFileBytes = maxFileBytes;
            _maxRollFiles = Math.Max(1, maxRollFiles);
        }

        public void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                Directory.CreateDirectory(_crashDir);
            }
            catch
            {
                // Fallback to base directory if cannot create the target crash directory
                try { Directory.CreateDirectory(AppDomain.CurrentDomain.BaseDirectory); } catch { }
            }

            // Initialize enhanced diagnostics services
            try
            {
                _startupDiagnostics = StartupDiagnosticsService.Instance;
                _startupDiagnostics.LogCheckpoint("GlobalExceptionHandler.Initialize", "Starting enhanced exception handling initialization");
                
                _assemblyMonitor = AssemblyLoadingMonitor.Instance;
                _assemblyMonitor.StartMonitoring();
                
                _startupDiagnostics.LogCheckpoint("GlobalExceptionHandler.Initialize", "Assembly loading monitoring started");
            }
            catch (Exception ex)
            {
                SafeWrite("startup", $"Failed to initialize enhanced diagnostics: {ex.Message}");
            }

            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            Dispatcher.CurrentDispatcher.UnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            _isInitialized = true;
            SafeWrite("startup", "Enhanced GlobalExceptionHandler initialized with comprehensive crash logging");
            
            try
            {
                _startupDiagnostics?.LogCheckpoint("GlobalExceptionHandler.Initialize", "Exception handlers registered successfully");
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
                Dispatcher.CurrentDispatcher.UnhandledException -= OnDispatcherUnhandledException;
                TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
                
                // Stop enhanced diagnostics services
                _assemblyMonitor?.StopMonitoring();
                _assemblyMonitor?.ReportToGlobalHandler();
                
                _startupDiagnostics?.LogCheckpoint("GlobalExceptionHandler.Dispose", "Exception handling services disposed");
            }
            catch { /* ignore */ }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            
            // Log to both legacy and enhanced crash logging
            SafeWrite("dispatcher", FormatExceptionBlock("DispatcherUnhandledException", e.Exception, id));
            
            try
            {
                CrashLoggingService.Instance.LogCrash(
                    e.Exception, 
                    "DispatcherUnhandledException", 
                    "Unhandled exception in UI thread", 
                    false);
            }
            catch { }
            
            // Keep app running, consistent with existing behavior.
            e.Handled = true;

            TryNotifyUser("Unhandled UI exception", e.Exception, id);
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            var id = Guid.NewGuid().ToString("N")[..8];

            if (ex != null)
            {
                // Log to both legacy and enhanced crash logging
                SafeWrite("appdomain", FormatExceptionBlock("AppDomain.UnhandledException", ex, id, e.IsTerminating));
                
                try
                {
                    CrashLoggingService.Instance.LogCrash(
                        ex, 
                        "AppDomain.UnhandledException", 
                        "Unhandled exception in application domain", 
                        e.IsTerminating);
                }
                catch { }
                
                TryNotifyUser("Unhandled fatal exception", ex, id);
            }
            else
            {
                SafeWrite("appdomain", $"[{Now()}] AppDomain.UnhandledException (non-Exception object). Terminating={e.IsTerminating}");
                
                try
                {
                    var nonExceptionCrash = new InvalidOperationException(
                        $"AppDomain.UnhandledException with non-Exception object. Terminating={e.IsTerminating}. Object type: {e.ExceptionObject?.GetType().FullName ?? "<null>"}");
                    
                    CrashLoggingService.Instance.LogCrash(
                        nonExceptionCrash, 
                        "AppDomain.UnhandledException", 
                        "Non-Exception object thrown", 
                        e.IsTerminating);
                }
                catch { }
            }
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            // Keep consistency with existing code where task exceptions generally are not fatal
            var id = Guid.NewGuid().ToString("N")[..8];
            
            // Log to both legacy and enhanced crash logging
            SafeWrite("task", FormatExceptionBlock("TaskScheduler.UnobservedTaskException", e.Exception, id));
            
            try
            {
                CrashLoggingService.Instance.LogCrash(
                    e.Exception, 
                    "TaskScheduler.UnobservedTaskException", 
                    "Unobserved exception in background task", 
                    false);
            }
            catch { }
            
            e.SetObserved();
            // Optional: do not notify user for background task noise; comment next line if desired.
            // TryNotifyUser("Background task exception", e.Exception, id);
        }

        private void TryNotifyUser(string title, Exception ex, string correlationId)
        {
            try
            {
                // Avoid blocking or crashing the UI; keep message short.
                MessageBox.Show(
                    $"{title} (ID: {correlationId}){Environment.NewLine}{ex.Message}",
                    "Unhandled Exception",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* non-fatal */ }
        }

        private string FormatExceptionBlock(string source, Exception ex, string correlationId, bool? isTerminating = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{Now()}] {source} | ID={correlationId}");
            if (isTerminating.HasValue)
            {
                sb.AppendLine($"Terminating={isTerminating.Value}");
            }
            AppendEnvironmentInfo(sb);
            AppendExceptionInfo(sb, ex);
            return sb.ToString();
        }

        private void AppendEnvironmentInfo(StringBuilder sb)
        {
            try
            {
                var proc = Process.GetCurrentProcess();
                sb.AppendLine($"ProcessId={proc.Id} Name={proc.ProcessName}");
            }
            catch { /* ignore */ }

            try
            {
                sb.AppendLine($"AppBase={AppDomain.CurrentDomain.BaseDirectory}");
                sb.AppendLine($".NET={Environment.Version}");
                sb.AppendLine($"OS={Environment.OSVersion} 64bitOS={Environment.Is64BitOperatingSystem} 64bitProc={Environment.Is64BitProcess}");
                sb.AppendLine($"Culture={CultureInfo.CurrentCulture.Name} UI={CultureInfo.CurrentUICulture.Name}");
                sb.AppendLine($"CmdLine={Environment.CommandLine}");
            }
            catch { /* ignore */ }

            sb.AppendLine();
        }

        private void AppendExceptionInfo(StringBuilder sb, Exception ex)
        {
            try
            {
                var list = Flatten(ex).ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    var (e, depth) = list[i];
                    sb.AppendLine($"-- Exception[{depth}] {e.GetType().FullName}");
                    sb.AppendLine($"Message: {e.Message}");
                    sb.AppendLine($"Source: {e.Source}");
                    sb.AppendLine("Stack:");
                    sb.AppendLine(e.StackTrace ?? "<no stack>");
                    if (e.Data is not null && e.Data.Count > 0)
                    {
                        sb.AppendLine("Data:");
                        foreach (var key in e.Data.Keys)
                        {
                            try { sb.AppendLine($"  {key}: {e.Data[key]}"); } catch { }
                        }
                    }
                    sb.AppendLine();
                }
            }
            catch
            {
                sb.AppendLine("<Failed to format exception>");
            }
        }

        private IEnumerable<(Exception ex, int depth)> Flatten(Exception ex, int depth = 0)
        {
            if (ex == null) yield break;
            yield return (ex, depth);

            if (ex is AggregateException ae)
            {
                foreach (var inner in ae.InnerExceptions)
                    foreach (var item in Flatten(inner, depth + 1))
                        yield return item;
            }
            else if (ex.InnerException is not null)
            {
                foreach (var item in Flatten(ex.InnerException, depth + 1))
                    yield return item;
            }
        }

        private void SafeWrite(string prefix, string content)
        {
            try
            {
                var file = Path.Combine(_crashDir, $"{prefix}.log");
                RollIfNeeded(file);
                File.AppendAllText(file, content + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Last-chance fallback to base directory
                try
                {
                    var fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{prefix}-fallback.log");
                    File.AppendAllText(fallback, content + Environment.NewLine, Encoding.UTF8);
                }
                catch { /* give up */ }
            }
        }

        private void RollIfNeeded(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var len = new FileInfo(path).Length;
                if (len < _maxFileBytes) return;

                for (int i = _maxRollFiles - 1; i >= 0; i--)
                {
                    var src = i == 0 ? path : $"{path}.{i}";
                    var dst = $"{path}.{i + 1}";
                    if (File.Exists(dst)) File.Delete(dst);
                    if (File.Exists(src)) File.Move(src, dst);
                }
            }
            catch
            {
                // ignore rolling errors
            }
        }

        private static string Now() => DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
    }
}
