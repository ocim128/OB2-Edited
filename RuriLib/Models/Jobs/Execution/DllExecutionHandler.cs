using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RuriLib.Helpers.CSharp;
using RuriLib.Logging;
using RuriLib.Models.Bots;

namespace RuriLib.Models.Jobs.Execution;

/// <summary>
/// Handles execution of DLL-based configs
/// </summary>
public class DllExecutionHandler : IBotExecutionHandler
{
    public async Task<Dictionary<string, object>> ExecuteAsync(BotData botData, MultiRunInput input, CancellationToken cancellationToken)
    {
        var outputVariables = new Dictionary<string, object>();
        
        botData.Logger.Log("Executing DLL config...", LogColors.Yellow);
        
        var scriptGlobals = new ScriptGlobals(botData, input.Globals);
        
        // Set custom inputs answers
        foreach (var answer in input.CustomInputsAnswers)
        {
            (scriptGlobals.input as IDictionary<string, object>).Add(answer.Key, answer.Value);
        }
        
        var task = (Task)input.DLLMethod.Invoke(null, new object[]
        {
            botData,
            scriptGlobals.input,
            scriptGlobals.globals,
            outputVariables,
            cancellationToken
        });

        await task.ConfigureAwait(false);
        botData.Logger.Log("DLL config executed.", LogColors.Yellow);
        
        return outputVariables;
    }
}