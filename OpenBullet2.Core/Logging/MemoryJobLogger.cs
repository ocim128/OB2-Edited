using OpenBullet2.Core.Models.Settings;
using OpenBullet2.Core.Services;
using RuriLib.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace OpenBullet2.Logging;

public struct JobLogEntry
{
    public LogKind kind;
    public string message;
    public string color;
    public DateTime date;

    public JobLogEntry(LogKind kind, string message, string color)
    {
        this.kind = kind;
        this.message = message;
        this.color = color;
        date = DateTime.Now;
    }
}

/// <summary>
/// An in-memory logger for job operations.
/// </summary>
public class MemoryJobLogger
{
    // Use a thread-safe dictionary mapping jobId to a concurrent queue of log entries.
    private readonly ConcurrentDictionary<int, ConcurrentQueue<JobLogEntry>> logs = new();
    private readonly OpenBulletSettings settings;
    public event EventHandler<int> NewLog; // The integer is the id of the job for which a new log came

    public MemoryJobLogger(OpenBulletSettingsService settingsService)
    {
        settings = settingsService.Settings;
    }

    /// <summary>
    /// Returns a snapshot of the log for a given job.
    /// </summary>
    public IEnumerable<JobLogEntry> GetLog(int jobId)
    {
        if (logs.TryGetValue(jobId, out var queue))
        {
            return queue.ToArray();
        }
        return Array.Empty<JobLogEntry>();
    }

    /// <summary>
    /// Logs a message for a job. If logging is disabled, nothing happens.
    /// The log is stored in a thread-safe queue that is trimmed to a maximum size.
    /// </summary>
    public void Log(int jobId, string message, LogKind kind = LogKind.Custom, string color = "white")
    {
        if (!settings.GeneralSettings.EnableJobLogging)
        {
            return;
        }

        var entry = new JobLogEntry(kind, message, color);
        var maxBufferSize = settings.GeneralSettings.LogBufferSize;

        // Get or add a new concurrent queue for this job.
        var queue = logs.GetOrAdd(jobId, _ => new ConcurrentQueue<JobLogEntry>());
        queue.Enqueue(entry);

        // Trim the queue if it exceeds the maximum buffer size.
        // (This is a best-effort approach; in high-concurrency scenarios, some extra entries might momentarily persist.)
        while (maxBufferSize > 0 && queue.Count > maxBufferSize)
        {
            queue.TryDequeue(out _);
        }

        NewLog?.Invoke(this, jobId);
    }

    public void LogInfo(int jobId, string message) => Log(jobId, message, LogKind.Info, "var(--fg-primary)");
    public void LogSuccess(int jobId, string message) => Log(jobId, message, LogKind.Success, "var(--fg-hit)");
    public void LogWarning(int jobId, string message) => Log(jobId, message, LogKind.Warning, "var(--fg-custom)");
    public void LogError(int jobId, string message) => Log(jobId, message, LogKind.Error, "var(--fg-fail)");

    /// <summary>
    /// Clears the log for the specified job.
    /// </summary>
    public void Clear(int jobId)
    {
        if (logs.TryGetValue(jobId, out var queue))
        {
            while (queue.TryDequeue(out _)) { }
        }
    }
}
