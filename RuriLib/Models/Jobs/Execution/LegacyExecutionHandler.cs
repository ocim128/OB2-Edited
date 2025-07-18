using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RuriLib.Legacy.LS;
using RuriLib.Legacy.Models;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Variables;

namespace RuriLib.Models.Jobs.Execution;

/// <summary>
/// Handles execution of legacy LoliScript configs
/// </summary>
public class LegacyExecutionHandler : IBotExecutionHandler
{
    public async Task<Dictionary<string, object>> ExecuteAsync(BotData botData, MultiRunInput input, CancellationToken cancellationToken)
    {
        var outputVariables = new Dictionary<string, object>();
        
        botData.Logger.Log("Executing legacy LoliScript config...", LogColors.Yellow);
        
        var lsGlobals = new LSGlobals(botData)
        {
            Globals = input.LegacyGlobals,
            GlobalCookies = input.LegacyGlobalCookies
        };
        
        var loliScript = new LoliScript(input.LegacyLoliScript);

        do
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await loliScript.TakeStep(lsGlobals);
        }
        while (loliScript.CanProceed);
        
        botData.Logger.Log("Legacy LoliScript config executed.", LogColors.Yellow);
        
        // Extract output variables from legacy variables
        var legacyVariables = botData.TryGetObject<VariablesList>("legacyVariables");
        if (legacyVariables != null)
        {
            foreach (var variable in legacyVariables.Variables.Where(static v => v.MarkedForCapture))
            {
                switch (variable.Type)
                {
                    case VariableType.String:
                        outputVariables[variable.Name] = variable.AsString();
                        break;

                    case VariableType.ListOfStrings:
                        outputVariables[variable.Name] = variable.AsListOfStrings();
                        break;

                    case VariableType.DictionaryOfStrings:
                        outputVariables[variable.Name] = variable.AsDictionaryOfStrings();
                        break;
                        
                    case VariableType.Int:
                    case VariableType.Float:
                    case VariableType.Bool:
                    case VariableType.ByteArray:
                        // These types are not captured in legacy mode
                        break;
                        
                    default:
                        break;
                }
            }
        }
        
        return outputVariables;
    }
}