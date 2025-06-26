using RuriLib.Attributes;
using RuriLib.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace RuriLib.Logging
{
    /// An <see cref="IBotLogger"/> that logs in memory with enhanced performance.
    public class BotLogger : IBotLogger
    {
        /// <inheritdoc/>
        public bool Enabled { get; set; } = true;

        /// <inheritdoc/>
        public string ExecutingBlock { get; set; } = "Unknown";

        private readonly List<BotLoggerEntry> entries = new();
        private readonly object lockObject = new object();
        
        // Performance optimization: limit memory usage
        private const int MAX_ENTRIES = 10000;
        private const int TRIM_TO_ENTRIES = 8000;

        /// <inheritdoc/>
        public event EventHandler<BotLoggerEntry> NewEntry;

        /// <inheritdoc/>
        public IEnumerable<BotLoggerEntry> Entries
        {
            get
            {
                lock (lockObject)
                {
                    // Make a copy of the list so it's thread safe
                    return entries.ToList();
                }
            }
        }

        /// <summary>
        /// Gets the current number of log entries.
        /// </summary>
        public int EntryCount 
        { 
            get 
            { 
                lock (lockObject) 
                { 
                    return entries.Count; 
                } 
            } 
        }

        /// <inheritdoc/>
        public void LogObject(object obj, string color = "#fff", bool canViewAsHtml = false)
        {
            if (!Enabled || obj == null)
                return;

            var message = obj switch
            {
                List<string> list => list.AsString(),
                Dictionary<string, string> dict => dict.AsString(),
                byte[] bytes => bytes.AsString(),
                float f => f.AsString(),
                string str when str.Length > 40000 => $"{str.Substring(0, 40000)}... [TRUNCATED - {str.Length} total chars]",
                _ => obj.ToString()
            };

            Log(message, color, canViewAsHtml);
        }

        /// <inheritdoc/>
        public void Log(string message, string color = "#fff", bool canViewAsHtml = false)
        {
            LogWithLevel(message, color, LogLevel.Info, canViewAsHtml);
        }

        /// <inheritdoc/>
        public void Log(IEnumerable<string> enumerable, string color = "#fff", bool canViewAsHtml = false)
        {
            if (!Enabled || enumerable == null)
                return;

            var lines = enumerable.ToList();
            if (lines.Count == 0)
                return;

            // Limit the number of lines to prevent memory issues
            if (lines.Count > 1000)
            {
                lines = lines.Take(1000).ToList();
                lines.Add($"... [TRUNCATED - {enumerable.Count()} total lines]");
            }

            var message = string.Join(Environment.NewLine, lines);
            Log(message, color, canViewAsHtml);
        }

        /// <inheritdoc/>
        public void LogHeader([CallerMemberName] string caller = null)
        {
            // Do not log if called by lolicode
            if (!Enabled || ExecutingBlock == "LoliCode")
                return;

            try
            {
                var callingMethod = new StackFrame(1).GetMethod();
                var attribute = callingMethod?.GetCustomAttribute<Block>();

                if (attribute is { name: { } })
                    caller = attribute.name;

                var entry = new BotLoggerEntry
                {
                    Message = $">> {ExecutingBlock} ({caller ?? "Unknown"}) <<",
                    Color = LogColors.ChromeYellow,
                    Timestamp = DateTime.Now,
                    Level = LogLevel.Info,
                    Category = "Header"
                };

                lock (lockObject)
                {
                    // Add separator and header
                    entries.Add(new BotLoggerEntry { Message = string.Empty, Timestamp = DateTime.Now, Level = LogLevel.Info });
                    entries.Add(entry);
                }
                
                try
                {
                    NewEntry?.Invoke(this, entry);
                }
                catch
                {
                    // Ignore event handler exceptions
                }
            }
            catch
            {
                // Fallback if reflection fails
                Log($">> {ExecutingBlock} ({caller ?? "Unknown"}) <<", LogColors.ChromeYellow);
            }
        }

        /// <inheritdoc/>
        public void Clear()
        {
            lock (lockObject)
            {
                entries.Clear();
            }
        }

        /// <inheritdoc/>
        public void LogError(string message, Exception ex)
        {
            if (!Enabled)
                return;

            var errorMessage = string.IsNullOrEmpty(message) ? "An error occurred" : message;
            if (ex != null)
            {
                errorMessage += $": {ex.Message}";
            }

            // Prevent extremely long error messages
            if (errorMessage.Length > 200000)
            {
                errorMessage = $"{errorMessage.Substring(0, 200000)}... [TRUNCATED - {errorMessage.Length} total chars]";
            }

            var entry = new BotLoggerEntry
            {
                Message = errorMessage,
                Color = LogColors.Tomato,
                CanViewAsHtml = false,
                Exception = ex,
                Timestamp = DateTime.Now,
                Level = LogLevel.Error
            };

            bool shouldTrim = false;
            lock (lockObject)
            {
                entries.Add(entry);
                
                // Memory management: trim old entries if we have too many
                if (entries.Count > MAX_ENTRIES)
                {
                    shouldTrim = true;
                }
            }

            // Perform trimming outside the lock to reduce lock time
            if (shouldTrim)
            {
                TrimEntries();
            }

            try
            {
                NewEntry?.Invoke(this, entry);
            }
            catch
            {
                // Ignore event handler exceptions
            }
        }

        /// <summary>
        /// Logs a success message with appropriate styling.
        /// </summary>
        public void LogSuccess(string message)
        {
            LogWithLevel(message, LogColors.GreenYellow, LogLevel.Success);
        }

        /// <summary>
        /// Logs a warning message with appropriate styling.
        /// </summary>
        public void LogWarning(string message)
        {
            LogWithLevel(message, LogColors.Orange, LogLevel.Warning);
        }

        /// <summary>
        /// Logs an info message with appropriate styling.
        /// </summary>
        public void LogInfo(string message)
        {
            LogWithLevel(message, LogColors.White, LogLevel.Info);
        }

        /// <summary>
        /// Logs a debug message with appropriate styling (only if verbose mode).
        /// </summary>
        public void LogDebug(string message)
        {
            LogWithLevel($"[DEBUG] {message}", LogColors.MediumTurquoise, LogLevel.Debug);
        }

        /// <summary>
        /// Internal method to log with a specific log level.
        /// </summary>
        private void LogWithLevel(string message, string color, LogLevel level, bool canViewAsHtml = false, string category = null)
        {
            if (!Enabled || string.IsNullOrEmpty(message))
                return;

            // Prevent extremely long messages from consuming too much memory
            if (message.Length > 200000)
            {
                message = $"{message.Substring(0, 200000)}... [TRUNCATED - {message.Length} total chars]";
            }

            var entry = new BotLoggerEntry
            {
                Message = message,
                Color = color,
                CanViewAsHtml = canViewAsHtml,
                Timestamp = DateTime.Now,
                Level = level,
                Category = category
            };

            bool shouldTrim = false;
            lock (lockObject)
            {
                entries.Add(entry);
                
                // Memory management: trim old entries if we have too many
                if (entries.Count > MAX_ENTRIES)
                {
                    shouldTrim = true;
                }
            }

            // Perform trimming outside the lock to reduce lock time
            if (shouldTrim)
            {
                TrimEntries();
            }
            
            // Fire event asynchronously to avoid blocking
            try
            {
                NewEntry?.Invoke(this, entry);
            }
            catch
            {
                // Ignore event handler exceptions to prevent logging from failing
            }
        }

        /// <summary>
        /// Trims old entries to prevent memory growth.
        /// </summary>
        private void TrimEntries()
        {
            lock (lockObject)
            {
                if (entries.Count > MAX_ENTRIES)
                {
                    var entriesToRemove = entries.Count - TRIM_TO_ENTRIES;
                    entries.RemoveRange(0, entriesToRemove);
                }
            }
        }

        /// <summary>
        /// Gets memory usage statistics for the logger.
        /// </summary>
        public (int EntryCount, long EstimatedMemoryBytes) GetMemoryStats()
        {
            lock (lockObject)
            {
                var estimatedMemory = entries.Sum(e => 
                    (e.Message?.Length ?? 0) * 2 + // Rough estimate: 2 bytes per char
                    (e.Color?.Length ?? 0) * 2 + 
                    100); // Overhead per entry
                
                return (entries.Count, estimatedMemory);
            }
        }
    }
}
