using AngleSharp.Text;
using RuriLib.Helpers;
using RuriLib.Logging;
using RuriLib.Models.Configs;
using RuriLib.Models.Data;
using RuriLib.Models.Proxies;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RuriLib.Models.Bots;

public class BotData(Providers providers, ConfigSettings configSettings, IBotLogger logger,
    DataLine line, Proxy? proxy = null, bool useProxy = false)
{
    public DataLine Line { get; set; } = line;
    public Proxy? Proxy { get; set; } = proxy;
    public bool UseProxy { get; set; } = useProxy;

    public ConfigSettings ConfigSettings { get; } = configSettings;
    public Providers Providers { get; } = providers;
    public IBotLogger Logger { get; set; } = logger;

    // Use thread-safe RNG per bot via provider; no change in API, but document usage.
    public Random Random { get; } = providers.RNG.GetNew();

    public CancellationToken CancellationToken { get; set; }
    public AsyncLocker? AsyncLocker { get; set; }
    public Stepper? Stepper { get; set; }
    public decimal CaptchaCredit { get; set; } = 0;
    public string ExecutionInfo { get; set; } = "IDLE";

    /// <summary>
    /// Fixed properties
    /// </summary>
    public string STATUS { get; set; } = "NONE";
    public string SOURCE { get; set; } = string.Empty;
    public byte[] RAWSOURCE { get; set; } = [];
    public string ADDRESS { get; set; } = string.Empty;
    public int RESPONSECODE { get; set; }
    public Dictionary<string, string> COOKIES { get; set; } = [];
    public Dictionary<string, string> HEADERS { get; set; } = [];
    public string ERROR { get; set; } = string.Empty;
    public int BOTNUM { get; set; }

    [Obsolete("Do not use this property, it's only here for retro compatibility but it can cause memory leaks." +
              " Use the SetObject and TryGetObject methods instead!")]
    public Dictionary<string, object> Objects { get; } = [];

    /// <summary>
    /// This list will hold the names of all variables that are marked for capture
    /// </summary>
    public List<string> MarkedForCapture { get; } = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void LogVariableAssignment(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (Logger.Enabled)
            Logger.Log($"Assigned value to variable '{name}'", LogColors.Yellow);
    }

    public void MarkForCapture(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be null or empty");
        }

        // Avoid Contains+Add double lookup by linear scan; list is typically very small.
        foreach (var existing in MarkedForCapture)
        {
            if (existing == name)
            {
                return;
            }
        }

        MarkedForCapture.Add(name);
        if (Logger.Enabled)
            Logger.Log($"Variable '{name}' marked for capture", LogColors.Tomato);
    }

    public void UnmarkCapture(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be null or empty");
        }

        // Remove without Contains double-lookup
        for (int i = 0; i < MarkedForCapture.Count; i++)
        {
            if (MarkedForCapture[i] == name)
            {
                MarkedForCapture.RemoveAt(i);
                if (Logger.Enabled)
                    Logger.Log($"Variable '{name}' removed from capture", LogColors.Yellow);
                return;
            }
        }
    }

    public void ExecutingBlock(string label)
    {
        ExecutionInfo = $"Executing block {label}";
        if (Logger != null)
        {
            Logger.LogBlockStart(label);
        }
    }

    public void ResetState()
    {
        ExecutionInfo = "Retrying";
        STATUS = "NONE";
        SOURCE = string.Empty;
        RAWSOURCE = [];
        ADDRESS = string.Empty;
        ERROR = string.Empty;
        RESPONSECODE = 0;
        COOKIES.Clear();
        HEADERS.Clear();
        MarkedForCapture.Clear();

        if (Logger.Enabled)
            Logger.Log("Resetting bot state and disposing objects for retry.", LogColors.Yellow);

        // Dispose all runtime-created objects except the selected ones
        DisposeObjectsExcept(["puppeteer", "puppeteerPage", "puppeteerFrame", "httpClient", "ironPyEngine"]);
    }

    public void SetObject(string name, object obj, bool disposeExisting = true)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name cannot be null or empty", nameof(name));
        if (obj is null) throw new ArgumentNullException(nameof(obj));

        if (Objects.TryGetValue(name, out var existing) && existing is IDisposable d && disposeExisting)
        {
            if (Logger.Enabled)
                Logger.Log($"Disposing existing object '{name}'", LogColors.Yellow);
            d.Dispose();
        }

        Objects[name] = obj;

        // Avoid cluttering verbose log with common system-managed objects
        if (ConfigSettings.GeneralSettings.VerboseMode && Logger.Enabled && !IsSuppressedObject(name))
        {
            Logger.Log($"Set object '{name}'", LogColors.DarkGreen);
        }
    }

    private static bool IsSuppressedObject(string name)
        => name is "httpClient" or "ironPyEngine";

    public T? TryGetObject<T>(string name) where T : class
    {
        if (Objects.TryGetValue(name, out var value) && value is T t)
        {
            if (ConfigSettings.GeneralSettings.VerboseMode && Logger.Enabled && !IsFrequentInternal(name))
            {
                Logger.Log($"Retrieved object '{name}'", LogColors.DarkGreen);
            }
            return t;
        }

        // Only log missing for system-managed objects
        if (IsSystemManaged(name) && Logger.Enabled)
        {
            Logger.Log($"Could not retrieve object '{name}'", LogColors.DarkRed);
        }

        return null;
    }

    private static bool IsFrequentInternal(string name)
        => name is "httpClient" or "ironPyEngine" or "puppeteer" or "puppeteerPage" or "puppeteerFrame" or "selenium" or "seleniumDriver";

    private static bool IsSystemManaged(string name)
        => name is "httpClient" or "ironPyEngine";

    public void DisposeObjectsExcept(string[]? except = null)
    {
        var exclusions = except ?? Array.Empty<string>();

        // Avoid LINQ allocations in hot paths
        foreach (var kvp in Objects)
        {
            // skip excluded
            var key = kvp.Key;
            var value = kvp.Value;
            if (value is not IDisposable d) continue;

            var excluded = false;
            for (int i = 0; i < exclusions.Length; i++)
            {
                if (exclusions[i] == key)
                {
                    excluded = true;
                    break;
                }
            }
            if (excluded) continue;

            try
            {
                d.Dispose();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to dispose of object {key}: {ex.Message}", ex);
            }
        }
    }
}
