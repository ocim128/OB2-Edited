using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RuriLib.Helpers.CSharp;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Custom;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RuriLib.Helpers.Transpilers
{
    /// <summary>
    /// Takes care of transpiling a list of blocks to a C# script.
    /// </summary>
    public class Stack2CSharpTranspiler
    {
        /// <summary>
        /// Transpiles a list of <paramref name="blocks"/> to a C# script. If <paramref name="pauseToken"/> is
        /// not null, step-by-step mode will be enabled.
        /// </summary>
        public static string Transpile(List<BlockInstance> blocks, ConfigSettings settings, bool stepByStep = false)
        {
            var declaredVariables = typeof(BotData).GetProperties()
                .Select(p => $"data.{p.Name}").ToList();

            using var writer = new StringWriter();

            var validBlocks = blocks.Where(b => !b.Disabled);

            // OPTIMIZED: Only detect variables from specific block types that commonly reference variables
            // This is much faster than the comprehensive approach that was causing performance issues
            var detectedVariables = new HashSet<string>();

            foreach (var block in validBlocks)
            {
                // Only check blocks that commonly have variable references to avoid expensive ToLC() calls
                switch (block)
                {
                    case LoliCodeBlockInstance loli when !string.IsNullOrEmpty(loli.Script):
                        detectedVariables.UnionWith(VariableDetector.DetectFromLoliCodeStatement(loli.Script));
                        break;

                    case ScriptBlockInstance script when !string.IsNullOrEmpty(script.InputVariables):
                        // Add input variables from script blocks
                        foreach (var rawInput in script.InputVariables.Split(','))
                        {
                            var input = rawInput.Trim();
                            if (!string.IsNullOrWhiteSpace(input) && input != "input" && input != "globals" && input != "data")
                            {
                                detectedVariables.Add(input);
                            }
                        }
                        break;

                    case AutoBlockInstance auto:
                        // Add output variable if defined
                        if (!string.IsNullOrEmpty(auto.OutputVariable))
                        {
                            detectedVariables.Add(auto.OutputVariable);
                        }
                        
                        // Special handling for CreateMultiple blocks
                        if (auto.Descriptor.Id == "CreateMultiple")
                        {
                            var createMultipleVars = GetCreateMultipleVariables(auto);
                            detectedVariables.UnionWith(createMultipleVars);
                        }
                        break;
                }
            }

            // Emit NullDynamic declarations for detected variables
            var missingVars = VariableDetector.GetMissingVariables(detectedVariables, declaredVariables);
            foreach (var varName in missingVars)
            {
                writer.WriteLine($"dynamic {varName} = RuriLib.Models.NullDynamic.Instance;");
                declaredVariables.Add(varName);
            }

            // Pre-declare all AutoBlockInstance output variables to ensure method-scope visibility
            var outputVars = blocks.OfType<AutoBlockInstance>().Select(b => b.OutputVariable).Distinct();
            foreach (var varName in outputVars)
            {
                if (!declaredVariables.Contains(varName))
                {
                    writer.WriteLine($"dynamic {varName} = RuriLib.Models.NullDynamic.Instance;");
                    declaredVariables.Add(varName);
                }
            }
            writer.WriteLine();

            foreach (var block in validBlocks)
            {
                writer.WriteLine($"// BLOCK: {block.Label}");
                writer.WriteLine($"data.ExecutingBlock({CSharpWriter.SerializeString(block.Label)});");

                var snippet = block.ToCSharp(declaredVariables, settings);
                var tree = CSharpSyntaxTree.ParseText(snippet);
                writer.WriteLine(tree.GetRoot().NormalizeWhitespace().ToFullString());
                
                // Special handling for CreateMultiple blocks - assign variables from data object
                if (block is AutoBlockInstance auto && auto.Descriptor.Id == "CreateMultiple")
                {
                    var createMultipleVars = GetCreateMultipleVariables(auto);
                    foreach (var varName in createMultipleVars)
                    {
                        writer.WriteLine($"{varName} = data.Objects.ContainsKey(\"{varName}\") ? data.Objects[\"{varName}\"] : RuriLib.Models.NullDynamic.Instance;");
                    }
                }
                
                writer.WriteLine();

                // If in step by step mode, and if not the last block, check if pause was requested
                if (stepByStep && block != validBlocks.Last())
                {
                    writer.WriteLine("await data.Stepper.WaitForStepAsync(data.CancellationToken);");
                }
            }

            return writer.ToString();
        }
        
        /// <summary>
        /// Extracts variable names that will be created by a CreateMultiple block
        /// </summary>
        private static List<string> GetCreateMultipleVariables(AutoBlockInstance block)
        {
            var variables = new List<string>();
            
            if (block.Descriptor.Id != "CreateMultiple")
                return variables;
                
            // Extract variable names from the block settings
            for (int i = 1; i <= 5; i++)
            {
                var varNameKey = $"variableName{i}";
                if (block.Settings.TryGetValue(varNameKey, out var setting))
                {
                    var strSetting = setting.FixedSetting as StringSetting;
                    if (strSetting != null && !string.IsNullOrWhiteSpace(strSetting.Value))
                    {
                        variables.Add(strSetting.Value);
                    }
                }
            }
            
            return variables;
        }
    }
}
