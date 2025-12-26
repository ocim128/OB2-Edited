using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RuriLib.Helpers.CSharp;
using RuriLib.Logging;
using RuriLib.Models.Bots;

namespace RuriLib.Models.Jobs.Execution;

/// <summary>
/// Handles execution of compiled C# script configs
/// </summary>
public class ScriptExecutionHandler : IBotExecutionHandler
{
    public async Task<Dictionary<string, object>> ExecuteAsync(BotData botData, MultiRunInput input, CancellationToken cancellationToken)
    {
        var outputVariables = new Dictionary<string, object>();
        
        botData.Logger.Log("Executing compiled script config...", LogColors.Yellow);
        
        var scriptGlobals = new ScriptGlobals(botData, input.Globals);
        
        // Set custom inputs answers
        foreach (var answer in input.CustomInputsAnswers)
        {
            (scriptGlobals.input as IDictionary<string, object>).Add(answer.Key, answer.Value);
        }
        
        var variables = await input.Script.RunAsync(scriptGlobals, cancellationToken).ConfigureAwait(false);
        botData.Logger.Log("Compiled script config executed.", LogColors.Yellow);
        
        // Extract output variables from script state
        if (variables != null)
        {
            foreach (var kvp in variables)
            {
                if (botData.MarkedForCapture.Contains(kvp.Key))
                {
                    outputVariables[kvp.Key] = kvp.Value;
                }
            }
        }
        
        return outputVariables;
    }
}