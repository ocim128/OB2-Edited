using System;
using System.Collections.Generic;
using System.Linq;

namespace RuriLib.Logging
{
    /// <summary>
    /// Utility class for analyzing and filtering log entries.
    /// </summary>
    public static class LogAnalyzer
    {
        /// <summary>
        /// Filters log entries by level.
        /// </summary>
        public static IEnumerable<BotLoggerEntry> FilterByLevel(IEnumerable<BotLoggerEntry> entries, LogLevel level)
        {
            return entries.Where(e => e.Level == level);
        }

        /// <summary>
        /// Filters log entries by multiple levels.
        /// </summary>
        public static IEnumerable<BotLoggerEntry> FilterByLevels(IEnumerable<BotLoggerEntry> entries, params LogLevel[] levels)
        {
            var levelSet = new HashSet<LogLevel>(levels);
            return entries.Where(e => levelSet.Contains(e.Level));
        }

        /// <summary>
        /// Gets only error entries.
        /// </summary>
        public static IEnumerable<BotLoggerEntry> GetErrors(IEnumerable<BotLoggerEntry> entries)
        {
            return entries.Where(e => e.IsError);
        }

        /// <summary>
        /// Gets only warning entries.
        /// </summary>
        public static IEnumerable<BotLoggerEntry> GetWarnings(IEnumerable<BotLoggerEntry> entries)
        {
            return entries.Where(e => e.IsWarning);
        }

        /// <summary>
        /// Gets only success entries.
        /// </summary>
        public static IEnumerable<BotLoggerEntry> GetSuccesses(IEnumerable<BotLoggerEntry> entries)
        {
            return entries.Where(e => e.IsSuccess);
        }

        /// <summary>
        /// Filters entries by time range.
        /// </summary>
        public static IEnumerable<BotLoggerEntry> FilterByTimeRange(IEnumerable<BotLoggerEntry> entries, DateTime start, DateTime end)
        {
            return entries.Where(e => e.Timestamp >= start && e.Timestamp <= end);
        }

        /// <summary>
        /// Filters entries by category.
        /// </summary>
        public static IEnumerable<BotLoggerEntry> FilterByCategory(IEnumerable<BotLoggerEntry> entries, string category)
        {
            return entries.Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Searches entries by message content.
        /// </summary>
        public static IEnumerable<BotLoggerEntry> SearchByMessage(IEnumerable<BotLoggerEntry> entries, string searchTerm, bool caseSensitive = false)
        {
            if (string.IsNullOrEmpty(searchTerm))
                return entries;

            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return entries.Where(e => e.Message?.Contains(searchTerm, comparison) == true);
        }

        /// <summary>
        /// Gets log statistics.
        /// </summary>
        public static LogStatistics GetStatistics(IEnumerable<BotLoggerEntry> entries)
        {
            var entryList = entries.ToList();
            var stats = new LogStatistics
            {
                TotalEntries = entryList.Count,
                ErrorCount = entryList.Count(e => e.IsError),
                WarningCount = entryList.Count(e => e.IsWarning),
                SuccessCount = entryList.Count(e => e.IsSuccess),
                InfoCount = entryList.Count(e => e.Level == LogLevel.Info),
                DebugCount = entryList.Count(e => e.Level == LogLevel.Debug)
            };

            if (entryList.Count > 0)
            {
                stats.FirstEntry = entryList.Min(e => e.Timestamp);
                stats.LastEntry = entryList.Max(e => e.Timestamp);
                stats.Duration = stats.LastEntry - stats.FirstEntry;
                stats.AverageMessageLength = entryList.Average(e => e.MessageLength);
                stats.TotalEstimatedMemory = entryList.Sum(e => e.EstimatedMemoryBytes);
            }

            return stats;
        }

        /// <summary>
        /// Gets the most recent entries up to a specified count.
        /// </summary>
        public static IEnumerable<BotLoggerEntry> GetRecent(IEnumerable<BotLoggerEntry> entries, int count)
        {
            return entries.OrderByDescending(e => e.Timestamp).Take(count);
        }

        /// <summary>
        /// Groups entries by log level.
        /// </summary>
        public static Dictionary<LogLevel, List<BotLoggerEntry>> GroupByLevel(IEnumerable<BotLoggerEntry> entries)
        {
            return entries.GroupBy(e => e.Level).ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// Groups entries by category.
        /// </summary>
        public static Dictionary<string, List<BotLoggerEntry>> GroupByCategory(IEnumerable<BotLoggerEntry> entries)
        {
            return entries.GroupBy(e => e.Category ?? "Uncategorized").ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// Finds entries with exceptions.
        /// </summary>
        public static IEnumerable<BotLoggerEntry> GetEntriesWithExceptions(IEnumerable<BotLoggerEntry> entries)
        {
            return entries.Where(e => e.Exception != null);
        }

        /// <summary>
        /// Gets entries that exceed a certain message length.
        /// </summary>
        public static IEnumerable<BotLoggerEntry> GetLongMessages(IEnumerable<BotLoggerEntry> entries, int minLength = 1000)
        {
            return entries.Where(e => e.MessageLength >= minLength);
        }
    }

    /// <summary>
    /// Statistics about a collection of log entries.
    /// </summary>
    public class LogStatistics
    {
        public int TotalEntries { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public int SuccessCount { get; set; }
        public int InfoCount { get; set; }
        public int DebugCount { get; set; }
        public DateTime FirstEntry { get; set; }
        public DateTime LastEntry { get; set; }
        public TimeSpan Duration { get; set; }
        public double AverageMessageLength { get; set; }
        public long TotalEstimatedMemory { get; set; }

        /// <summary>
        /// Gets the error rate as a percentage.
        /// </summary>
        public double ErrorRate => TotalEntries > 0 ? (double)ErrorCount / TotalEntries * 100 : 0;

        /// <summary>
        /// Gets the warning rate as a percentage.
        /// </summary>
        public double WarningRate => TotalEntries > 0 ? (double)WarningCount / TotalEntries * 100 : 0;

        /// <summary>
        /// Gets the success rate as a percentage.
        /// </summary>
        public double SuccessRate => TotalEntries > 0 ? (double)SuccessCount / TotalEntries * 100 : 0;

        /// <summary>
        /// Gets a human-readable summary.
        /// </summary>
        public override string ToString()
        {
            return $"Total: {TotalEntries}, Errors: {ErrorCount} ({ErrorRate:F1}%), " +
                   $"Warnings: {WarningCount} ({WarningRate:F1}%), " +
                   $"Success: {SuccessCount} ({SuccessRate:F1}%), " +
                   $"Duration: {Duration:hh\\:mm\\:ss}, " +
                   $"Memory: {TotalEstimatedMemory / 1024.0:F1} KB";
        }
    }
} 