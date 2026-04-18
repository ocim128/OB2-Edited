using Flux.Core.Services;
using Flux.Native.Services;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Custom;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;


namespace Flux.Native.Utils
{
    public static class Suggestions
    {
        public static IEnumerable<string> GetInputVariableSuggestions(BlockSetting setting)
        {
            var debuggerVM = App.ServiceProvider.GetRequiredService<ViewModelsService>().Debugger;
            var rlSettings = App.ServiceProvider.GetRequiredService<RuriLibSettingsService>();
            var configService = App.ServiceProvider.GetRequiredService<ConfigService>();

            List<string> suggestions = [
            "data.SOURCE", "data.ERROR", "data.ADDRESS",
            "data.HEADERS[\"name\"]", "data.COOKIES[\"name\"]",
            "data.STATUS", "data.RESPONSECODE", "data.RAWSOURCE", "data.Line.Data" ];

            var wordlistTypeName = debuggerVM.WordlistType;
            var wordlistType = rlSettings.Environment.WordlistTypes
                .FirstOrDefault(w => w.Name == wordlistTypeName)
                ?? throw new InvalidOperationException($"Wordlist type '{wordlistTypeName}' not found in settings.");

            // Collect prefix items separately, then combine -- avoids O(n^2) List.Insert(0, ...)
            var prefix = new List<string>();
            foreach (var slice in wordlistType.Slices.Concat(wordlistType.SlicesAlias))
            {
                prefix.Add($"input.{slice}");
            }

            var stack = configService.SelectedConfig.Stack;

            var blockVariables = new HashSet<string>();
            foreach (var block in stack)
            {
                // If it's the current block, stop here (we don't want to add variables from this or the next blocks)
                if (block.Settings.Any(s => s.Value == setting))
                {
                    break;
                }

                foreach (var variable in GetOutputVariables(block))
                {
                    if (!string.IsNullOrWhiteSpace(variable) && !suggestions.Contains(variable) && !blockVariables.Contains(variable))
                    {
                        blockVariables.Add(variable);
                    }
                }
            }

            // Final order: block variables (closest to current block first), then wordlist slices, then defaults
            var blockVariablesList = blockVariables.ToList();
            blockVariablesList.Reverse();
            var result = new List<string>(blockVariablesList.Count + prefix.Count + suggestions.Count);
            result.AddRange(blockVariablesList);
            result.AddRange(prefix);
            result.AddRange(suggestions);
            return result;
        }

        private static IEnumerable<string> GetOutputVariables(BlockInstance block)
            => block switch
            {
                AutoBlockInstance x => x.Descriptor.ReturnType == null ? [] : [x.OutputVariable],
                ParseBlockInstance x => [x.OutputVariable],
                ScriptBlockInstance x => x.OutputVariables.Select(v => v.Name),
                _ => []
            };
    }
}
