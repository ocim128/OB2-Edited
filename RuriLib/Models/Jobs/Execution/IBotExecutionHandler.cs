using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RuriLib.Models.Bots;

namespace RuriLib.Models.Jobs.Execution;

/// <summary>
/// Interface for handling bot execution in different modes
/// </summary>
public interface IBotExecutionHandler
{
    /// <summary>
    /// Executes the bot with the given data
    /// </summary>
    Task<Dictionary<string, object>> ExecuteAsync(BotData botData, MultiRunInput input, CancellationToken cancellationToken);
}