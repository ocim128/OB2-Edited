using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RuriLib.Helpers.CSharp;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Custom;
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
                writer.WriteLine();

                // If in step by step mode, and if not the last block, check if pause was requested
                if (stepByStep && block != validBlocks.Last())
                {
                    writer.WriteLine("await data.Stepper.WaitForStepAsync(data.CancellationToken);");
                }
            }

            return writer.ToString();
        }
    }
}
