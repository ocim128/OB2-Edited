using RuriLib.Helpers.CSharp;
using RuriLib.Helpers.LoliCode;
using RuriLib.Models.Blocks;
using RuriLib.Models.Configs;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Helpers.Blocks;

namespace RuriLib.Helpers.Transpilers
{
    /// <summary>
    /// A faster transpiler that converts LoliCode directly to C# without building the full Block Stack.
    /// This reduces memory allocation and CPU usage for large configs.
    /// </summary>
    public static class FastLoli2CSharpTranspiler
    {
        public static string Transpile(string script, ConfigSettings settings)
        {
            if (string.IsNullOrWhiteSpace(script)) return "";

            using var writer = new StringWriter();
            var definedVariables = typeof(BotData).GetProperties().Select(p => $"data.{p.Name}").ToList();

            // Pass 1: Scan for all variables and labels to pre-declare them
            var detectedVariables = new HashSet<string>();
            var labels = new HashSet<string>();
            ScanScript(script, detectedVariables, labels);

            // Declare detected variables
             var missingVars = VariableDetector.GetMissingVariables(detectedVariables, definedVariables);
            foreach (var varName in missingVars)
            {
                writer.WriteLine($"dynamic {varName} = RuriLib.Models.NullDynamic.Instance;");
                definedVariables.Add(varName);
            }

            // Declare labels
            foreach (var label in labels)
            {
                writer.WriteLine($"int __jumpCount_{label} = 0;");
            }
            writer.WriteLine();

            // Pass 2: Transpile
            using var reader = new StringReader(script);
            string line;
            var jumpLabels = new Dictionary<string, int>();

            string currentBlockId = null;
            StringBuilder currentBlockScript = new StringBuilder();

            while ((line = reader.ReadLine()) != null)
            {
                var trimmedLine = line.Trim();
                
                // Check if we are ending a block
                if (currentBlockId != null && (trimmedLine.StartsWith("ENDBLOCK") || trimmedLine.StartsWith("BLOCK:")))
                {
                    // Process the accumulated block
                    try 
                    {
                        var block = BlockFactory.GetBlock<BlockInstance>(currentBlockId);
                        var blockScript = currentBlockScript.ToString();
                        int dummyLine = 0;
                        block.FromLC(ref blockScript, ref dummyLine);
                        
                        // Skip disabled blocks - they should not be executed
                        if (block.Disabled)
                        {
                            writer.WriteLine($"// BLOCK (DISABLED): {block.Label ?? currentBlockId}");
                        }
                        else
                        {
                            writer.WriteLine($"// BLOCK: {block.Label ?? currentBlockId}");
                            writer.WriteLine($"data.ExecutingBlock({CSharpWriter.SerializeString(block.Label ?? currentBlockId)});");
                            writer.WriteLine(block.ToCSharp(definedVariables, settings));
                            
                            if (block is AutoBlockInstance auto && auto.Descriptor.Id == "CreateMultiple")
                            {
                                var createMultipleVars = GetCreateMultipleVariables(auto);
                                foreach (var varName in createMultipleVars)
                                {
                                    writer.WriteLine($"{varName} = data.Objects.ContainsKey(\"{varName}\") ? data.Objects[\"{varName}\"] : RuriLib.Models.NullDynamic.Instance;");
                                }
                            }

                            writer.WriteLine();
                        }
                    }
                    catch (Exception ex)
                    {
                        writer.WriteLine($"// ERROR Transpiling Block {currentBlockId}: {ex.Message}");
                    }

                    // Reset
                    currentBlockId = null;
                    currentBlockScript.Clear();

                    if (trimmedLine.StartsWith("ENDBLOCK")) continue; // Consumed
                    // If it was BLOCK:, we fall through to the next check which handles start of block
                }

                // Check if we are starting a block
                if (TryParseBlockDirective(trimmedLine, out var blockId))
                {
                    currentBlockId = blockId;
                    continue;
                }

                // If inside a block, accumulate
                if (currentBlockId != null)
                {
                    currentBlockScript.AppendLine(line);
                    continue;
                }

                // If we are here, we are in "Script" mode (statements)
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("//")) 
                {
                    writer.WriteLine(line);
                    continue;
                }

                try {
                    writer.WriteLine(LoliCodeStatementTranspiler.TranspileStatement(trimmedLine, definedVariables, settings, ref jumpLabels));
                } catch (NotSupportedException) {
                    writer.WriteLine(line);
                }
            }
            
            // Handle EOF inside a block
            if (currentBlockId != null)
            {
                try 
                {
                    var block = BlockFactory.GetBlock<BlockInstance>(currentBlockId);
                    var blockScript = currentBlockScript.ToString();
                    int dummyLine = 0;
                    block.FromLC(ref blockScript, ref dummyLine);
                    
                    // Skip disabled blocks - they should not be executed
                    if (block.Disabled)
                    {
                        writer.WriteLine($"// BLOCK (DISABLED): {block.Label ?? currentBlockId}");
                    }
                    else
                    {
                        writer.WriteLine($"// BLOCK: {block.Label ?? currentBlockId}");
                        writer.WriteLine($"data.ExecutingBlock({CSharpWriter.SerializeString(block.Label ?? currentBlockId)});");
                        writer.WriteLine(block.ToCSharp(definedVariables, settings));
                        writer.WriteLine();
                    }
                }
                catch (Exception ex)
                {
                    writer.WriteLine($"// ERROR Transpiling Block {currentBlockId}: {ex.Message}");
                }
            }

            return writer.ToString();
        }

        private static void ScanScript(string script, HashSet<string> detectedVariables, HashSet<string> labels)
        {
            using var reader = new StringReader(script);
            string line;
            
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//")) continue;

                // Detect Labels
                var labelMatch = Regex.Match(trimmed, @"^#(\w+)$");
                if (labelMatch.Success) labels.Add(labelMatch.Groups[1].Value);

                var jumpMatch = Regex.Match(trimmed, @"JUMP\s+#(\w+)");
                if (jumpMatch.Success) labels.Add(jumpMatch.Groups[1].Value);

                // Detect Output Variables (=> VAR @name)
                var outMatch = Regex.Match(trimmed, @"^=> (VAR|CAP) @?(\w+)");
                if (outMatch.Success) detectedVariables.Add(outMatch.Groups[2].Value);
                
                // Inspect CreateMultiple setting assignments
                // variableName1 = "varName"
                var multiMatch = Regex.Match(trimmed, @"variableName\d+\s*=\s*""(\w+)""");
                if (multiMatch.Success) detectedVariables.Add(multiMatch.Groups[1].Value);

                // General LoliCode detection
                detectedVariables.UnionWith(VariableDetector.DetectFromLoliCodeStatement(trimmed));
            }
        }
        
        private static bool TryParseBlockDirective(string line, out string blockId)
        {
            blockId = string.Empty;
            if (!line.StartsWith("BLOCK:", StringComparison.Ordinal)) return false;
            
            var token = line.Substring(6).Trim();
            if (string.IsNullOrWhiteSpace(token)) return false;
            
            blockId = token;
            return true;
        }

        private static List<string> GetCreateMultipleVariables(AutoBlockInstance block)
        {
            var variables = new List<string>();
            // Extract variable names from the block settings
            for (int i = 1; i <= 10; i++)
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
    
    public static class StringReaderExtensions
    {
        public static string PeekLine(this StringReader reader)
        {
            // StringReader doesn't have PeekLine, but we can reflect or just read/unread? 
            // Actually strictly speaking StringReader is not rewindable easily.
            // But we know we are parsing in order.
            // Alternatively, implement the loop in Transpile better.
            return null; 
        }
    }
}
