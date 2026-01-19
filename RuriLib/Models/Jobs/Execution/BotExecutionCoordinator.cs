using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Scripting;
using RuriLib.Helpers;
using RuriLib.Functions.Http;

using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Captchas;
using RuriLib.Models.Configs;
using RuriLib.Models.Jobs.Status;
using RuriLib.Models.Variables;

namespace RuriLib.Models.Jobs.Execution;

/// <summary>
/// Coordinates the execution of a single bot instance
/// </summary>
public class BotExecutionCoordinator
{
    private readonly IBotExecutionHandler _executionHandler;
    private readonly ProxyManager _proxyManager;
    private static readonly HashSet<string> ExceptObjects = new(["httpClient", "ironPyEngine"], StringComparer.Ordinal);

    public BotExecutionCoordinator(IBotExecutionHandler executionHandler, ProxyManager proxyManager)
    {
        _executionHandler = executionHandler ?? throw new ArgumentNullException(nameof(executionHandler));
        _proxyManager = proxyManager; // Allow null when proxies are disabled
    }

    /// <summary>
    /// Executes a bot with the given input and returns the result
    /// </summary>
    public async Task<CheckResult> ExecuteAsync(MultiRunInput input, CancellationToken cancellationToken)
    {
        var botData = input.BotData;
        botData.CancellationToken = cancellationToken;

        // Validate data
        if (!IsDataValid(botData))
        {
            return CreateInvalidResult(botData);
        }

        var outputVariables = new Dictionary<string, object>();
        SetupBotData(botData, input);

        // Main execution loop with retry logic
        var executionResult = await ExecuteWithRetryLogicAsync(input, outputVariables, cancellationToken);

        return new CheckResult
        {
            BotData = botData,
            OutputVariables = executionResult.OutputVariables
        };
    }

    private static bool IsDataValid(BotData botData)
    {
        return botData.Line.IsValid &&
               botData.Line.RespectsRules(botData.ConfigSettings.DataSettings.DataRules);
    }

    private static CheckResult CreateInvalidResult(BotData botData)
    {
        botData.STATUS = BotStatus.Invalid;
        return new CheckResult
        {
            BotData = botData,
            OutputVariables = new Dictionary<string, object>()
        };
    }



    private static void SetupBotData(BotData botData, MultiRunInput input)
    {
        // Add this BotData to the array for detailed MultiRunJob display mode
        var botIndex = (int)(input.Index++ % input.Job.Bots);
        input.Job.CurrentBotDatas[botIndex] = botData;
        botData.BOTNUM = botIndex + 1;
    }

    private async Task<ExecutionResult> ExecuteWithRetryLogicAsync(MultiRunInput input,
        Dictionary<string, object> outputVariables, CancellationToken cancellationToken)
    {
        var botData = input.BotData;
        var maxRetries = 100; // Allow more retries for ERROR/RETRY statuses
        var retryCount = 0;
        Exception lastCachedException = null;
        bool cachedIsNetworkException = false;

        while (retryCount < maxRetries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await ExecuteSingleAttemptAsync(input, outputVariables, cancellationToken);

            // Cache network exception check to avoid multiple calls for same exception
            bool isNetworkException = false;
            if (result.LastException != null)
            {
                if (!ReferenceEquals(result.LastException, lastCachedException))
                {
                    // New exception - update cache
                    lastCachedException = result.LastException;
                    cachedIsNetworkException = NetworkExceptionHelper.IsNetworkException(result.LastException);
                }
                isNetworkException = cachedIsNetworkException;
            }

            if (ShouldRetry(result.Status, botData, input.Job))
            {
                retryCount++;
                await HandleRetryAsync(botData, result.Status, input.Job, isNetworkException, retryCount);
                continue;
            }

            if (ShouldHandleBanOrError(result.Status, botData, input.Job))
            {
                retryCount++;
                if (HandleBanOrError(botData, input.Job))
                {
                    // Use the same delay logic as HandleRetryAsync to avoid duplication
                    int delayMs = isNetworkException
                        ? NetworkExceptionHelper.CalculateBackoffDelay(retryCount - 1)
                        : FixedRetryDelayMs;
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, cancellationToken);
                    }
                    continue; // Retry
                }
            }

            return result;
        }

        // Max retries exceeded
        botData.STATUS = BotStatus.Error;
        return new ExecutionResult { Status = botData.STATUS, OutputVariables = outputVariables };
    }

    private async Task<ExecutionResult> ExecuteSingleAttemptAsync(MultiRunInput input,
        Dictionary<string, object> outputVariables, CancellationToken cancellationToken)
    {
        var botData = input.BotData;
        var lastException = (Exception)null;

        try
        {
            botData.ResetState();
            botData.Proxy = null;
            botData.UseProxy = ProxyManager.ShouldUseProxies(input.Job.ProxyMode, botData.ConfigSettings.ProxySettings);

            // Get proxy if needed
            if (_proxyManager != null && !await _proxyManager.TryGetProxyAsync(botData, input.ProxyPool, cancellationToken))
            {
                botData.STATUS = BotStatus.Error;
                lastException = new Exception("Failed to get proxy");
                return new ExecutionResult { Status = botData.STATUS, OutputVariables = outputVariables, LastException = lastException };
            }

            LogBotStart(botData, input.Job.Config.Mode);

            // Execute the config
            var executionOutputs = await _executionHandler.ExecuteAsync(botData, input, cancellationToken);

            // Merge outputs
            foreach (var kvp in executionOutputs)
            {
                outputVariables[kvp.Key] = kvp.Value;
            }
        }
        catch (CompilationErrorException)
        {
            // Bubble up script compilation errors so the job can stop before consuming more data
            throw;
        }
        catch (Exception ex)
        {
            botData.STATUS = BotStatus.Error;
            lastException = ex;
            botData.Logger.Log($"[{botData.ExecutionInfo}] {ex.GetType().Name}: {ex.Message}", LogColors.Tomato);
            input.Job.Statistics.IncrementErrors();
        }
        finally
        {
            HandleBotCleanup(botData);
            _proxyManager?.ReleaseProxy(botData, input.ProxyPool);
        }

        return new ExecutionResult { Status = botData.STATUS, OutputVariables = outputVariables, LastException = lastException };
    }

    private static bool ShouldRetry(string status, BotData botData, MultiRunJob job)
    {
        if (status is not BotStatus.Retry and not BotStatus.Error)
        {
            return false;
        }

        // Handle captcha reporting on retry
        if (botData.ConfigSettings.GeneralSettings.ReportLastCaptchaOnRetry)
        {
            _ = Task.Run(async () => await ReportBadCaptchaAsync(botData));
        }

        return true;
    }

    // Cache fixed delay value to avoid repeated allocations
    private const int FixedRetryDelayMs = 0;

    // Cache common backoff delays to avoid repeated calculations
    private static readonly int[] CachedBackoffDelays = new int[20];

    static BotExecutionCoordinator()
    {
        // Pre-calculate common backoff delays for performance
        for (int i = 0; i < CachedBackoffDelays.Length; i++)
        {
            CachedBackoffDelays[i] = NetworkExceptionHelper.CalculateBackoffDelay(i);
        }
    }

    private static Task HandleRetryAsync(BotData botData, string status, MultiRunJob job, bool isNetworkException, int retryCount)
    {
        job.DebugLog($"RETRY ({botData.Line.Data})({botData.Proxy})");
        job.Statistics.IncrementRetried();

        // Calculate delay based on whether it's a network exception
        int delayMs;
        if (isNetworkException)
        {
            // Use cached delay for common retry counts, calculate for higher counts
            var retryIndex = retryCount - 1;
            delayMs = retryIndex < CachedBackoffDelays.Length
                ? CachedBackoffDelays[retryIndex]
                : NetworkExceptionHelper.CalculateBackoffDelay(retryIndex);
        }
        else
        {
            delayMs = FixedRetryDelayMs;
        }

        return delayMs <= 0
            ? Task.CompletedTask
            : Task.Delay(delayMs);
    }

    private static bool ShouldHandleBanOrError(string status, BotData botData, MultiRunJob job)
    {
        return BotStatus.ShouldBan(status);
    }

    private static bool HandleBanOrError(BotData botData, MultiRunJob job)
    {
        botData.Line.Retries++;
        var evasion = botData.ConfigSettings.ProxySettings.BanLoopEvasion;

        if (evasion > 0 && botData.Line.Retries > evasion)
        {
            botData.STATUS = BotStatus.None;
            job.DebugLog($"TO CHECK ON BAN LOOP EVASION ({botData.Line.Data})({botData.Proxy})");
            return false; // Don't retry
        }

        job.DebugLog($"BAN ({botData.Line.Data})({botData.Proxy})");
        job.Statistics.IncrementBanned();
        return true; // Retry
    }

    private static void LogBotStart(BotData botData, ConfigMode configMode)
    {
        botData.Logger.Log($"Trying to execute the config ({configMode})");
        botData.Logger.Log($"[{DateTime.Now.ToLongTimeString()}] BOT STARTED WITH DATA {botData.Line.Data} AND PROXY {(botData.Proxy?.ToString() ?? "NONE")}");
    }

    private static void HandleBotCleanup(BotData botData)
    {
        var endMessage = $"[{DateTime.Now.ToLongTimeString()}] BOT ENDED WITH STATUS: {botData.STATUS}";
        botData.ExecutingBlock(endMessage);
        botData.Logger.Log(endMessage);

        // Close the browser if needed
        if (botData.ConfigSettings.BrowserSettings.QuitBrowserStatuses.Contains(botData.STATUS))
        {
            botData.Logger.Log($"Disposing of browser objects since the bot STATUS was {botData.STATUS}", LogColors.Yellow);
            botData.DisposeObjectsExcept(ExceptObjects);
        }
        else
        {
            botData.Logger.Log("Disposing of browser objects except puppeteer, puppeteerPage, puppeteerFrame, httpClient, ironPyEngine", LogColors.Yellow);
            botData.DisposeObjectsExcept(BotData.DefaultExclusions);
        }

        // Cleanup TlsClient session if it exists to prevent native memory leaks
        if (botData.TlsClientSessionId.HasValue)
        {
            try
            {
                TlsClientRequestHandler.DestroySession(botData.TlsClientSessionId.Value);
            }
            catch { /* Ignore cleanup errors */ }
            botData.TlsClientSessionId = null;
        }
    }

    private static async Task ReportBadCaptchaAsync(BotData botData)
    {
        var lastCaptcha = botData.TryGetObject<CaptchaInfo>("lastCaptchaInfo");

        if (lastCaptcha is not null)
        {
            try
            {
                botData.ExecutingBlock("Reporting bad captcha upon RETRY status");
                botData.Logger.Log("Reporting bad captcha upon RETRY status...", LogColors.Yellow);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await botData.Providers.Captcha.ReportSolutionAsync(
                    lastCaptcha.Id, lastCaptcha.Type, false, cts.Token).ConfigureAwait(false);
                botData.ExecutingBlock("Bad captcha reported!");
                botData.Logger.Log("Bad captcha reported!", LogColors.Yellow);
            }
            catch (Exception ex)
            {
                botData.Logger.LogError("Failed to report bad captcha", ex);
            }
        }
    }

    private class ExecutionResult
    {
        public string Status { get; set; }
        public Dictionary<string, object> OutputVariables { get; set; }
        public Exception LastException { get; set; }
    }
}
