using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Views.Dialogs;

namespace OpenBullet2.Native.Infrastructure.Diagnostics
{
    /// <summary>
    /// Provides error recovery and reporting capabilities with graceful degradation
    /// to handle failures without causing application crashes.
    /// </summary>
    public sealed class ErrorRecoveryService
    {
        private static readonly Lazy<ErrorRecoveryService> _instance = new Lazy<ErrorRecoveryService>(() => new ErrorRecoveryService());
        public static ErrorRecoveryService Instance => _instance.Value;
        
        private readonly Dictionary<string, int> _errorCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, DateTime> _lastErrorTimes = new Dictionary<string, DateTime>();
        private readonly object _lockObject = new object();
        private readonly TimeSpan _errorThrottleWindow = TimeSpan.FromMinutes(5);
        private const int MaxErrorsPerWindow = 3;
        
        private ErrorRecoveryService()
        {
        }
        
        /// <summary>
        /// Attempts to recover from an error with graceful degradation.
        /// </summary>
        /// <param name="error">The exception that occurred</param>
        /// <param name="context">Context where the error occurred</param>
        /// <param name="recoveryAction">Optional recovery action to attempt</param>
        /// <param name="showUserNotification">Whether to show notification to user</param>
        /// <returns>True if recovery was successful or error was handled gracefully</returns>
        public bool TryRecover(Exception error, string context, Func<bool> recoveryAction = null, bool showUserNotification = true)
        {
            try
            {
                // Log the error for diagnostics
                CrashLoggingService.Instance.LogCrash(error, context, "Error recovery attempt", false);
                
                // Check if we should throttle error notifications
                if (ShouldThrottleError(context))
                {
                    return false;
                }
                
                // Attempt recovery action if provided
                bool recoverySuccessful = false;
                if (recoveryAction != null)
                {
                    try
                    {
                        recoverySuccessful = recoveryAction();
                        if (recoverySuccessful)
                        {
                            ResetErrorCount(context);
                            return true;
                        }
                    }
                    catch (Exception recoveryError)
                    {
                        CrashLoggingService.Instance.LogCrash(recoveryError, context, "Recovery action failed", false);
                    }
                }
                
                // Determine error severity and appropriate response
                var severity = DetermineErrorSeverity(error);
                var response = DetermineRecoveryResponse(error, context, severity);
                
                // Execute recovery response
                ExecuteRecoveryResponse(response, error, context, showUserNotification);
                
                return response.IsRecoverable;
            }
            catch (Exception recoveryException)
            {
                // Recovery itself failed - log and return false
                Debug.WriteLine($"Error recovery failed for {context}: {recoveryException.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Handles startup errors with specific recovery strategies.
        /// </summary>
        public bool TryRecoverStartupError(Exception error, string component)
        {
            var recoveryStrategies = new List<Func<bool>>();
            
            // Add component-specific recovery strategies
            switch (component.ToLowerInvariant())
            {
                case "database":
                    recoveryStrategies.Add(() => TryRecoverDatabase());
                    break;
                case "configuration":
                    recoveryStrategies.Add(() => TryRecoverConfiguration());
                    break;
                case "services":
                    recoveryStrategies.Add(() => TryRecoverServices());
                    break;
            }
            
            // Try each recovery strategy
            foreach (var strategy in recoveryStrategies)
            {
                if (TryRecover(error, $"Startup.{component}", strategy, false))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Creates a safe wrapper around an action that handles exceptions gracefully.
        /// </summary>
        public void ExecuteSafely(Action action, string context, bool showErrors = true)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                TryRecover(ex, context, null, showErrors);
            }
        }
        
        /// <summary>
        /// Creates a safe wrapper around a function that handles exceptions gracefully.
        /// </summary>
        public T ExecuteSafely<T>(Func<T> func, string context, T defaultValue = default(T), bool showErrors = true)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                TryRecover(ex, context, null, showErrors);
                return defaultValue;
            }
        }
        
        /// <summary>
        /// Creates a safe wrapper around an async action that handles exceptions gracefully.
        /// </summary>
        public async Task ExecuteSafelyAsync(Func<Task> action, string context, bool showErrors = true)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                TryRecover(ex, context, null, showErrors);
            }
        }
        
        private bool ShouldThrottleError(string context)
        {
            lock (_lockObject)
            {
                var now = DateTime.UtcNow;
                
                if (!_errorCounts.ContainsKey(context))
                {
                    _errorCounts[context] = 1;
                    _lastErrorTimes[context] = now;
                    return false;
                }
                
                var lastErrorTime = _lastErrorTimes[context];
                if (now - lastErrorTime > _errorThrottleWindow)
                {
                    // Reset counter if outside throttle window
                    _errorCounts[context] = 1;
                    _lastErrorTimes[context] = now;
                    return false;
                }
                
                _errorCounts[context]++;
                _lastErrorTimes[context] = now;
                
                return _errorCounts[context] > MaxErrorsPerWindow;
            }
        }
        
        private void ResetErrorCount(string context)
        {
            lock (_lockObject)
            {
                _errorCounts.Remove(context);
                _lastErrorTimes.Remove(context);
            }
        }
        
        private ErrorSeverity DetermineErrorSeverity(Exception error)
        {
            return error switch
            {
                OutOfMemoryException => ErrorSeverity.Critical,
                StackOverflowException => ErrorSeverity.Critical,
                AccessViolationException => ErrorSeverity.Critical,
                InvalidOperationException => ErrorSeverity.High,
                ArgumentException => ErrorSeverity.Medium,
                FileNotFoundException => ErrorSeverity.Medium,
                DirectoryNotFoundException => ErrorSeverity.Medium,
                UnauthorizedAccessException => ErrorSeverity.High,
                TimeoutException => ErrorSeverity.Medium,
                TaskCanceledException => ErrorSeverity.Low,
                OperationCanceledException => ErrorSeverity.Low,
                _ => ErrorSeverity.Medium
            };
        }
        
        private RecoveryResponse DetermineRecoveryResponse(Exception error, string context, ErrorSeverity severity)
        {
            return severity switch
            {
                ErrorSeverity.Critical => new RecoveryResponse
                {
                    IsRecoverable = false,
                    Action = RecoveryAction.ShowCriticalError,
                    Message = $"A critical error occurred in {context}. The application may need to be restarted.",
                    TechnicalDetails = error.ToString()
                },
                ErrorSeverity.High => new RecoveryResponse
                {
                    IsRecoverable = true,
                    Action = RecoveryAction.ShowError,
                    Message = $"An error occurred in {context}. Some functionality may be limited.",
                    TechnicalDetails = error.Message
                },
                ErrorSeverity.Medium => new RecoveryResponse
                {
                    IsRecoverable = true,
                    Action = RecoveryAction.ShowWarning,
                    Message = $"A minor issue occurred in {context}.",
                    TechnicalDetails = error.Message
                },
                ErrorSeverity.Low => new RecoveryResponse
                {
                    IsRecoverable = true,
                    Action = RecoveryAction.LogOnly,
                    Message = string.Empty,
                    TechnicalDetails = error.Message
                },
                _ => new RecoveryResponse
                {
                    IsRecoverable = true,
                    Action = RecoveryAction.ShowWarning,
                    Message = $"An unexpected error occurred in {context}.",
                    TechnicalDetails = error.Message
                }
            };
        }
        
        private void ExecuteRecoveryResponse(RecoveryResponse response, Exception error, string context, bool showUserNotification)
        {
            // Generate crash dump for critical errors
            if (response.Action == RecoveryAction.ShowCriticalError)
            {
                try
                {
                    _ = Task.Run(() => CrashDumpService.Instance.GenerateCrashDumpAsync(error, context, DumpType.MiniDumpWithData));
                }
                catch (Exception dumpEx)
                {
                    Debug.WriteLine($"Failed to generate crash dump for critical error: {dumpEx.Message}");
                }
            }
            
            if (!showUserNotification || response.Action == RecoveryAction.LogOnly)
            {
                return;
            }
            
            // Execute on UI thread if needed
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(() => ShowUserNotification(response, context));
            }
            else
            {
                ShowUserNotification(response, context);
            }
        }
        
        private void ShowUserNotification(RecoveryResponse response, string context)
        {
            try
            {
                switch (response.Action)
                {
                    case RecoveryAction.ShowCriticalError:
                        Alert.Error("Critical Error", response.Message);
                        break;
                    case RecoveryAction.ShowError:
                        Alert.Error("Error", response.Message);
                        break;
                    case RecoveryAction.ShowWarning:
                        Alert.Warning("Warning", response.Message);
                        break;
                }
            }
            catch
            {
                // If we can't show the alert, fall back to debug output
                Debug.WriteLine($"Failed to show user notification for {context}: {response.Message}");
            }
        }
        
        private bool TryRecoverDatabase()
        {
            try
            {
                // Attempt to verify database connection and recreate if needed
                
                // This would need to be implemented based on your specific database setup
                // For now, just return false to indicate recovery wasn't possible
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        private bool TryRecoverConfiguration()
        {
            try
            {
                // Check if appsettings.json exists and is readable
                var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var configPath = Path.Combine(appDirectory, "appsettings.json");
                
                if (File.Exists(configPath))
                {
                    // Try to read the file to verify it's not corrupted
                    var content = File.ReadAllText(configPath);
                    return !string.IsNullOrWhiteSpace(content);
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        private bool TryRecoverServices()
        {
            try
            {
                // This would need to be implemented based on your specific service architecture
                // For now, just return false to indicate recovery wasn't possible
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
    
    public enum ErrorSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
    
    public enum RecoveryAction
    {
        LogOnly,
        ShowWarning,
        ShowError,
        ShowCriticalError
    }
    
    public class RecoveryResponse
    {
        public bool IsRecoverable { get; set; }
        public RecoveryAction Action { get; set; }
        public string Message { get; set; }
        public string TechnicalDetails { get; set; }
    }
}
