using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RuriLib.Helpers;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs.Settings;
using RuriLib.Models.Jobs.Status;
using RuriLib.Models.Proxies;

namespace RuriLib.Models.Jobs;

/// <summary>
/// Manages proxy operations for a MultiRunJob
/// </summary>
public class ProxyManager
{
    private readonly MultiRunJob _job;
    private readonly AsyncLocker _asyncLocker;

    public ProxyManager(MultiRunJob job, AsyncLocker asyncLocker)
    {
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _asyncLocker = asyncLocker ?? throw new ArgumentNullException(nameof(asyncLocker));
    }

    /// <summary>
    /// Attempts to get a proxy for the bot, handling reload/unban logic
    /// </summary>
    public async Task<bool> TryGetProxyAsync(BotData botData, ProxyPool proxyPool, CancellationToken cancellationToken)
    {
        if (!botData.UseProxy)
        {
            return true;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (proxyPool)
            {
                botData.Proxy = proxyPool.GetProxy(_job.ConcurrentProxyMode,
                    botData.ConfigSettings.ProxySettings.MaxUsesPerProxy);
            }

            if (botData.Proxy != null)
            {
                return true;
            }

            botData.Logger.Log("No valid proxy found, trying to reload/unban...", LogColors.Yellow);
            
            if (_job.NoValidProxyBehaviour == NoValidProxyBehaviour.Reload)
            {
                await HandleProxyReloadAsync(botData, proxyPool, cancellationToken);
            }
            else if (_job.NoValidProxyBehaviour == NoValidProxyBehaviour.Unban)
            {
                HandleProxyUnban(botData, proxyPool);
            }
        }
    }

    /// <summary>
    /// Releases a proxy based on the bot status
    /// </summary>
    public void ReleaseProxy(BotData botData, ProxyPool proxyPool)
    {
        if (botData.Proxy == null)
        {
            return;
        }

        // If a ban status occurred, ban the proxy
        if (botData.ConfigSettings.ProxySettings.BanProxyStatuses.Contains(botData.STATUS))
        {
            _job.DebugLog($"Proxy {botData.Proxy} banned for status {botData.STATUS}");
            proxyPool.ReleaseProxy(botData.Proxy, !_job.NeverBanProxies);
        }
        // Otherwise set it to available
        else if (botData.Proxy.ProxyStatus == ProxyStatus.Busy)
        {
            _job.DebugLog($"Proxy {botData.Proxy} released as available");
            proxyPool.ReleaseProxy(botData.Proxy, false);
        }
    }

    private async Task HandleProxyReloadAsync(BotData botData, ProxyPool proxyPool, CancellationToken cancellationToken)
    {
        try
        {
            await _asyncLocker.Acquire(typeof(ProxyPool), nameof(ProxyPool.ReloadAllAsync), cancellationToken)
                .ConfigureAwait(false);

            botData.Logger.Log("Reloading proxies...", LogColors.Yellow);
            botData.Proxy = proxyPool.GetProxy(_job.ConcurrentProxyMode, 
                botData.ConfigSettings.ProxySettings.MaxUsesPerProxy);

            if (botData.Proxy == null)
            {
                await proxyPool.ReloadAllAsync(true, cancellationToken).ConfigureAwait(false);
                botData.Logger.Log("Proxies reloaded, trying to get a proxy again.", LogColors.Yellow);
            }
        }
        finally
        {
            _asyncLocker.Release(typeof(ProxyPool), nameof(ProxyPool.ReloadAllAsync));
        }
    }

    private static void HandleProxyUnban(BotData botData, ProxyPool proxyPool)
    {
        botData.Logger.Log("Unbanning proxies...", LogColors.Yellow);
        proxyPool.UnbanAll(TimeSpan.Zero); // Using TimeSpan.Zero as default
    }

    /// <summary>
    /// Determines if proxies should be used based on mode and settings
    /// </summary>
    public static bool ShouldUseProxies(JobProxyMode mode, ConfigProxySettings settings)
    {
        return mode switch
        {
            JobProxyMode.Default => settings.UseProxies,
            JobProxyMode.On => true,
            JobProxyMode.Off => false,
            _ => throw new NotImplementedException()
        };
    }
}
