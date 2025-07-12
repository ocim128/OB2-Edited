using AngleSharp.Text;
using RuriLib.Helpers;
using RuriLib.Logging;
using RuriLib.Models.Configs;
using RuriLib.Models.Data;
using RuriLib.Models.Proxies;
using System;
using System.Collections.Generic;
using System.Linq;
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

    public void LogVariableAssignment(string name)
        => Logger.Log($"Assigned value to variable '{name}'", LogColors.Yellow);

    public void MarkForCapture(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be null or empty");
        }

        if (MarkedForCapture.Contains(name))
        {
            return;
        }

        MarkedForCapture.Add(name);
        Logger.Log($"Variable '{name}' marked for capture", LogColors.Tomato);
    }

    public void UnmarkCapture(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be null or empty");
        }

        if (!MarkedForCapture.Contains(name))
        {
            return;
        }

        _ = MarkedForCapture.Remove(name);
        Logger.Log($"Variable '{name}' removed from capture", LogColors.Yellow);
    }

    public void ExecutingBlock(string label)
    {
        ExecutionInfo = $"Executing block {label}";

        if (Logger != null)
        {
            Logger.ExecutingBlock = label;
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

        // We need to dispose of objects created in each retry, because jobs should
        // only dispose of them after the bot has completed its work
        Logger.Log("Resetting bot state and disposing objects for retry.", LogColors.Yellow);
        DisposeObjectsExcept(["puppeteer", "puppeteerPage", "puppeteerFrame", "httpClient", "ironPyEngine"]);
    }

    public void SetObject(string name, object obj, bool disposeExisting = true)
    {
        if (Objects.TryGetValue(name, out var existing) && existing is IDisposable d && disposeExisting)
        {
            Logger.Log($"Disposing existing object '{name}'", LogColors.Yellow);
            d.Dispose();
        }

        Objects[name] = obj;
        // Avoid cluttering the verbose log with common system-managed objects
        var suppressedObjects = new[] { "httpClient", "ironPyEngine" };
        if (ConfigSettings.GeneralSettings.VerboseMode && !suppressedObjects.Contains(name))
        {
            Logger.Log($"Set object '{name}'", LogColors.DarkGreen);
        }
    }

    public T? TryGetObject<T>(string name) where T : class
    {
        if (Objects.TryGetValue(name, out var value) && value is T t)
        {
            if (ConfigSettings.GeneralSettings.VerboseMode)
            {
                // Avoid cluttering logs for frequently accessed internal objects
                var suppressedObjects = new[] { "httpClient", "ironPyEngine", "puppeteer", "puppeteerPage", "puppeteerFrame", "selenium", "seleniumDriver" };
                if (!suppressedObjects.Contains(name))
                {
                    Logger.Log($"Retrieved object '{name}'", LogColors.DarkGreen);
                }
            }
            return t;
        }

        // Only log if we tried to retrieve a system-managed object that was not found
        // The most common calls to TryGetObject are for system-managed objects
        // that are set by the framework (e.g. httpClient, ironPyEngine etc).
        // Browser objects are not necessarily supposed to be there and if they are not
        // it's not necessarily an error that should be logged.
        var systemManagedObjects = new[] { "httpClient", "ironPyEngine" };

        if (systemManagedObjects.Contains(name))
        {
            Logger.Log($"Could not retrieve object '{name}'", LogColors.DarkRed);
        }

        return null;
    }

    public void DisposeObjectsExcept(string[]? except = null)
    {
        except ??= [];

        foreach (var obj in Objects.Where(o => o.Value is IDisposable && !except.Contains(o.Key)))
        {
            try
            {
                (obj.Value as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to dispose of object {obj.Key}: {ex.Message}", ex);
            }
        }
    }
}
