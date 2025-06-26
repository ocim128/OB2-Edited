using System;
using System.Text.Json.Serialization;

namespace RuriLib.Logging
{
    /// <summary>
    /// An entry of a <see cref="BotLogger"/> with enhanced functionality.
    /// </summary>
    public class BotLoggerEntry
    {
        /// <summary>
        /// The logged message.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// The color of the message when displayed in a UI.
        /// </summary>
        public string Color { get; set; } = "#ffffff";

        /// <summary>
        /// Whether the message contains HTML code and can be rendered as HTML.
        /// </summary>
        public bool CanViewAsHtml { get; set; } = false;

        /// <summary>
        /// The date and time when the entry was added.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// The exception that was logged, if any.
        /// </summary>
        [JsonIgnore] // Don't serialize exceptions to avoid circular references
        public Exception? Exception { get; set; }

        /// <summary>
        /// The log level/type for categorization.
        /// </summary>
        public LogLevel Level { get; set; } = LogLevel.Info;

        /// <summary>
        /// Optional category or source of the log entry.
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// Gets the formatted timestamp as a string.
        /// </summary>
        [JsonIgnore]
        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");

        /// <summary>
        /// Gets the full formatted timestamp with date.
        /// </summary>
        [JsonIgnore]
        public string FullTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");

        /// <summary>
        /// Gets whether this entry represents an error.
        /// </summary>
        [JsonIgnore]
        public bool IsError => Level == LogLevel.Error || Exception != null;

        /// <summary>
        /// Gets whether this entry represents a warning.
        /// </summary>
        [JsonIgnore]
        public bool IsWarning => Level == LogLevel.Warning;

        /// <summary>
        /// Gets whether this entry represents success.
        /// </summary>
        [JsonIgnore]
        public bool IsSuccess => Level == LogLevel.Success;

        /// <summary>
        /// Gets the message length for statistics.
        /// </summary>
        [JsonIgnore]
        public int MessageLength => Message?.Length ?? 0;

        /// <summary>
        /// Gets a truncated version of the message for previews.
        /// </summary>
        /// <param name="maxLength">Maximum length of the preview</param>
        /// <returns>Truncated message</returns>
        public string GetPreview(int maxLength = 100)
        {
            if (string.IsNullOrEmpty(Message) || Message.Length <= maxLength)
                return Message ?? string.Empty;
            
            return Message.Substring(0, maxLength) + "...";
        }

        /// <summary>
        /// Gets a display-friendly string representation.
        /// </summary>
        public override string ToString()
        {
            var prefix = Level switch
            {
                LogLevel.Error => "[ERROR]",
                LogLevel.Warning => "[WARN]",
                LogLevel.Success => "[SUCCESS]",
                LogLevel.Debug => "[DEBUG]",
                _ => "[INFO]"
            };

            return $"{FormattedTimestamp} {prefix} {GetPreview(200)}";
        }

        /// <summary>
        /// Creates a copy of this entry with updated properties.
        /// </summary>
        public BotLoggerEntry Clone()
        {
            return new BotLoggerEntry
            {
                Message = Message,
                Color = Color,
                CanViewAsHtml = CanViewAsHtml,
                Timestamp = Timestamp,
                Exception = Exception,
                Level = Level,
                Category = Category
            };
        }

        /// <summary>
        /// Gets the estimated memory usage of this entry.
        /// </summary>
        [JsonIgnore]
        public long EstimatedMemoryBytes
        {
            get
            {
                return (Message?.Length ?? 0) * 2 + // Unicode chars = 2 bytes each
                       (Color?.Length ?? 0) * 2 +
                       (Category?.Length ?? 0) * 2 +
                       64; // Base overhead estimate
            }
        }
    }

    /// <summary>
    /// Defines the severity levels for log entries.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// Informational messages.
        /// </summary>
        Info = 0,

        /// <summary>
        /// Debug messages (verbose mode only).
        /// </summary>
        Debug = 1,

        /// <summary>
        /// Success messages.
        /// </summary>
        Success = 2,

        /// <summary>
        /// Warning messages.
        /// </summary>
        Warning = 3,

        /// <summary>
        /// Error messages.
        /// </summary>
        Error = 4
    }
}
