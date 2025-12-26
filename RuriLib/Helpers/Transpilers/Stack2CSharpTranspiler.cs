using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RuriLib.Helpers.CSharp;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Custom;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Blocks.Settings.Interpolated;
using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuriLib.Models.Blocks.Custom.HttpRequest;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;

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
                // Scan LoliCode script
                if (block is LoliCodeBlockInstance loli && !string.IsNullOrEmpty(loli.Script))
                {
                    detectedVariables.UnionWith(VariableDetector.DetectFromLoliCodeStatement(loli.Script));
                }

                // Scan Script block inputs
                if (block is ScriptBlockInstance script && !string.IsNullOrEmpty(script.InputVariables))
                {
                    // Add input variables from script blocks
                    foreach (var rawInput in script.InputVariables.Split(','))
                    {
                        var input = rawInput.Trim();
                        if (!string.IsNullOrWhiteSpace(input) && input != "input" && input != "globals" && input != "data")
                        {
                            detectedVariables.Add(input);
                        }
                    }
                }

                // Auto Block
                if (block is AutoBlockInstance auto)
                {
                    if (!string.IsNullOrEmpty(auto.OutputVariable))
                    {
                        detectedVariables.Add(auto.OutputVariable);
                    }
                    if (auto.Descriptor.Id == "CreateMultiple")
                    {
                        detectedVariables.UnionWith(GetCreateMultipleVariables(auto));
                    }
                }
                
                // Parse Block
                else if (block is ParseBlockInstance parse)
                {
                    if (!string.IsNullOrEmpty(parse.OutputVariable))
                    {
                        detectedVariables.Add(parse.OutputVariable);
                    }
                    foreach (var c in parse.ConditionalCases)
                    {
                        ScanSettings(c.Settings.Values, detectedVariables);
                    }
                }

                // HttpRequest Block
                else if (block is HttpRequestBlockInstance http)
                {
                    switch (http.RequestParams)
                    {
                        case StandardRequestParams x:
                            ScanSettings([x.Content, x.ContentType], detectedVariables);
                            break;
                        case RawRequestParams x:
                            ScanSettings([x.Content, x.ContentType], detectedVariables);
                            break;
                        case BasicAuthRequestParams x:
                            ScanSettings([x.Username, x.Password], detectedVariables);
                            break;
                        case MultipartRequestParams x:
                            ScanSettings([x.Boundary], detectedVariables);
                            foreach (var content in x.Contents)
                            {
                                switch (content)
                                {
                                    case StringHttpContentSettingsGroup y:
                                        ScanSettings([y.Name, y.Data, y.ContentType], detectedVariables);
                                        break;
                                    case RawHttpContentSettingsGroup y: // Raw content data is ByteArray, no interpolation/var scan needed usually? But Name/ContentType are strings
                                        ScanSettings([y.Name, y.ContentType], detectedVariables);
                                        break;
                                    case FileHttpContentSettingsGroup y:
                                        ScanSettings([y.Name, y.FileName, y.ContentType], detectedVariables);
                                        break;
                                }
                            }
                            break;
                    }
                }

                // Scan Settings (For ALL blocks)
                ScanSettings(block.Settings.Values, detectedVariables);

                // Keycheck Block specific
                if (block is KeycheckBlockInstance keycheck)
                {
                    foreach (var keychain in keycheck.Keychains)
                    {
                        foreach (var key in keychain.Keys)
                        {
                            ScanSettings(new[] { key.Left, key.Right }, detectedVariables);
                        }
                    }
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

            // Declare jump counters for loop detection
            var labels = new HashSet<string>();

            // Detect labels from label definitions (#LABEL)
            var labelDefs = new List<string>();
            foreach (var block in blocks.OfType<LoliCodeBlockInstance>())
            {
                if (!string.IsNullOrEmpty(block.Script))
                {
                    var lines = block.Script.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmedLine = line.Trim();
                        if (trimmedLine.StartsWith("#"))
                        {
                            var labelMatch = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"^#(\w+)$");
                            if (labelMatch.Success)
                            {
                                labelDefs.Add(labelMatch.Groups[1].Value);
                            }
                        }
                    }
                }
            }

            // Detect labels from JUMP statements (JUMP #LABEL) - search anywhere in script
            var labelJumps = new List<string>();
            foreach (var block in blocks.OfType<LoliCodeBlockInstance>())
            {
                if (!string.IsNullOrEmpty(block.Script))
                {
                    var matches = System.Text.RegularExpressions.Regex.Matches(block.Script, @"JUMP\s+#(\w+)");
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        labelJumps.Add(match.Groups[1].Value);
                    }
                }
            }

            labels.UnionWith(labelDefs);
            labels.UnionWith(labelJumps);

            foreach (var label in labels)
            {
                writer.WriteLine($"int __jumpCount_{label} = 0;");
            }
            writer.WriteLine();

            foreach (var block in validBlocks)
            {
                writer.WriteLine($"// BLOCK: {block.Label}");
                writer.WriteLine($"data.ExecutingBlock({CSharpWriter.SerializeString(block.Label)});");

                var snippet = block.ToCSharp(declaredVariables, settings);
                
                // OPTIMIZATION: Skip Roslyn normalization for simple blocks that generate clean C# code
                // This significantly improves transpilation performance for configs with many blocks
                if (IsSimpleBlock(block))
                {
                    // Simple blocks generate well-formatted code that doesn't need normalization
                    writer.WriteLine(snippet);
                }
                else
                {
                    // Complex blocks may generate code that benefits from formatting
                    var tree = CSharpSyntaxTree.ParseText(snippet);
                    writer.WriteLine(tree.GetRoot().NormalizeWhitespace().ToFullString());
                }

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
        /// Determines if a block generates simple, well-formatted C# code that doesn't need Roslyn normalization.
        /// Simple blocks are typically auto-generated blocks with straightforward method calls.
        /// Complex blocks (LoliCode, HttpRequest, Keycheck, Parse, Script) may generate more complex code
        /// that benefits from normalization for readability and consistency.
        /// </summary>
        private static bool IsSimpleBlock(BlockInstance block)
        {
            // AutoBlockInstance generates clean code: method calls with proper formatting
            // These are the most common block types and benefit most from skipping Roslyn
            if (block is AutoBlockInstance)
            {
                // CreateMultiple blocks are still simple
                // Safe mode blocks are still simple (just wrapped in try/catch)
                return true;
            }
            
            // These block types may generate complex or multi-line code that benefits from normalization:
            // - LoliCodeBlockInstance: User-written code mixed with transpiled statements
            // - HttpRequestBlockInstance: Complex async request building with multiple options
            // - KeycheckBlockInstance: Multiple nested conditions and keychains
            // - ParseBlockInstance: Regex parsing with multiple output patterns
            // - ScriptBlockInstance: External script execution with variable bindings
            // - ConditionalConstantStringBlockInstance: Complex switch/case generation
            
            return false;
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

        private static void ScanSettings(IEnumerable<BlockSetting> settings, HashSet<string> detectedVariables)
        {
            foreach (var setting in settings)
            {
                if (setting.InputMode == SettingInputMode.Variable && !string.IsNullOrEmpty(setting.InputVariableName))
                {
                    var baseVar = VariableDetector.ExtractBaseVariableName(setting.InputVariableName);
                    if (!string.IsNullOrEmpty(baseVar))
                    {
                        detectedVariables.Add(baseVar);
                    }
                }
                
                if (setting.InterpolatedSetting != null)
                {
                    switch (setting.InterpolatedSetting)
                    {
                        case InterpolatedStringSetting str:
                            detectedVariables.UnionWith(VariableDetector.DetectFromInterpolatedString(str.Value));
                            break;
                        case InterpolatedListOfStringsSetting list:
                            foreach (var val in list.Value)
                                detectedVariables.UnionWith(VariableDetector.DetectFromInterpolatedString(val));
                            break;
                        case InterpolatedDictionaryOfStringsSetting dict:
                            foreach (var kvp in dict.Value)
                            {
                                detectedVariables.UnionWith(VariableDetector.DetectFromInterpolatedString(kvp.Key));
                                detectedVariables.UnionWith(VariableDetector.DetectFromInterpolatedString(kvp.Value));
                            }
                            break;
                    }
                }
            }
        }
    }
}
