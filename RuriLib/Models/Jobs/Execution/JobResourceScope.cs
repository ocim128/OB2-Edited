using RuriLib.Helpers;
using RuriLib.Models.Data.Resources;
using RuriLib.Models.Jobs.Execution;
using RuriLib.Models.Proxies;
using RuriLib.Models.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;

namespace RuriLib.Models.Jobs;

internal sealed class JobResourceScope : IDisposable
{
    private readonly IReadOnlyList<ProxySource> proxySources;
    private bool disposed;
    private DateTime lastProxyStatsUpdate = DateTime.MinValue;
    private (int total, int alive, int banned, int bad) cachedProxyStats;
    private static readonly TimeSpan ProxyStatsCacheInterval = TimeSpan.FromSeconds(2);

    public JobResourceScope(JobInitializationResult initialization, IReadOnlyList<ProxySource> proxySources)
    {
        AsyncLocker = initialization.AsyncLocker;
        ExecutionCoordinator = initialization.ExecutionCoordinator;
        Resources = initialization.Resources;
        HttpClient = initialization.HttpClient;
        PythonEngine = initialization.PythonEngine;
        GlobalVariables = initialization.GlobalVariables;
        ProxyPool = initialization.ProxyPool;
        DllMethod = initialization.DllMethod;
        Script = initialization.Script;
        this.proxySources = proxySources;
    }

    public AsyncLocker AsyncLocker { get; }

    public BotExecutionCoordinator ExecutionCoordinator { get; }

    public Dictionary<string, ConfigResource> Resources { get; }

    public HttpClient HttpClient { get; }

    public Lazy<dynamic> PythonEngine { get; }

    public dynamic GlobalVariables { get; }

    public ProxyPool? ProxyPool { get; }

    public MethodInfo? DllMethod { get; }

    public IScript? Script { get; }

    public (int total, int alive, int banned, int bad) GetCachedProxyStats()
    {
        var now = DateTime.UtcNow;
        if (now - lastProxyStatsUpdate < ProxyStatsCacheInterval)
        {
            return cachedProxyStats;
        }

        if (ProxyPool?.Proxies == null)
        {
            cachedProxyStats = (0, 0, 0, 0);
        }
        else
        {
            var total = 0;
            var alive = 0;
            var banned = 0;
            var bad = 0;

            foreach (var proxy in ProxyPool.Proxies)
            {
                total++;
                switch (proxy.ProxyStatus)
                {
                    case ProxyStatus.Available:
                    case ProxyStatus.Busy:
                        alive++;
                        break;
                    case ProxyStatus.Banned:
                        banned++;
                        break;
                    case ProxyStatus.Bad:
                        bad++;
                        break;
                }
            }

            cachedProxyStats = (total, alive, banned, bad);
        }

        lastProxyStatsUpdate = now;
        return cachedProxyStats;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        HttpClient.Dispose();
        AsyncLocker.Dispose();
        ProxyPool?.Dispose();

        for (var i = 0; i < proxySources.Count; i++)
        {
            try
            {
                proxySources[i]?.Dispose();
            }
            catch
            {
                // Ignore disposal errors.
            }
        }

        foreach (var resource in Resources.Values.OfType<IDisposable>())
        {
            try
            {
                resource.Dispose();
            }
            catch
            {
                // Ignore disposal errors.
            }
        }

        disposed = true;
    }
}
