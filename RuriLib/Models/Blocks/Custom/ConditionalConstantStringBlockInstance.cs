using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RuriLib.Exceptions;
using RuriLib.Helpers.CSharp;
using RuriLib.Helpers.LoliCode;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Blocks.Parameters;
using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Configs;
using RuriLib.Models.Blocks.Settings.Interpolated;

namespace RuriLib.Models.Blocks.Custom;

/// <summary>
/// Auto block instance for Constant String with support for conditional overrides.
/// </summary>
public class ConditionalConstantStringBlockInstance : AutoBlockInstance
{
    private static readonly Regex keyRegex = new("^[A-Z]+KEY ", RegexOptions.Compiled);
    private static readonly StringParameter caseValueParameter = new()
    {
        Name = "conditionalValue",
        MultiLine = true
    };

    public List<ConditionalConstantStringCase> ConditionalCases { get; } = new();

    public ConditionalConstantStringBlockInstance(AutoBlockDescriptor descriptor)
        : base(descriptor)
    {
    }

    public override string ToLC(bool printDefaultParams = false)
    {
        var baseScript = base.ToLC(printDefaultParams);
        var writer = new LoliCodeWriter(baseScript);

        foreach (var conditionalCase in ConditionalCases)
        {
            var nameSetting = BlockSettingFactory.CreateStringSetting("caseName", conditionalCase.Name);
            writer
                .AppendToken("CASE", 2)
                .AppendToken(LoliCodeWriter.GetSettingValue(nameSetting))
                .AppendLine(conditionalCase.Mode.ToString());

            writer.AppendSetting(conditionalCase.Value, caseValueParameter, 4, printDefaults: true);

            foreach (var key in conditionalCase.Keys)
            {
                (string keyName, string comparison) = key switch
                {
                    BoolKey x => ("BOOLKEY", x.Comparison.ToString()),
                    StringKey x => ("STRINGKEY", x.Comparison.ToString()),
                    IntKey x => ("INTKEY", x.Comparison.ToString()),
                    FloatKey x => ("FLOATKEY", x.Comparison.ToString()),
                    DictionaryKey x => ("DICTKEY", x.Comparison.ToString()),
                    ListKey x => ("LISTKEY", x.Comparison.ToString()),
                    _ => throw new Exception("Unknown key type")
                };

                writer
                    .AppendToken(keyName, 4)
                    .AppendToken(LoliCodeWriter.GetSettingValue(key.Left))
                    .AppendToken(comparison)
                    .AppendLine(LoliCodeWriter.GetSettingValue(key.Right));
            }

            writer.AppendLine("ENDCASE", 2);
        }

        return writer.ToString();
    }

    public override void FromLC(ref string script, ref int lineNumber)
    {
        var sanitizedScript = ExtractCases(script, lineNumber);
        script = sanitizedScript;
        base.FromLC(ref script, ref lineNumber);
    }

    public override string ToCSharp(List<string> definedVariables, ConfigSettings settings)
    {
        if (!ConditionalCases.Any())
        {
            return base.ToCSharp(definedVariables, settings);
        }

        using var writer = new StringWriter();

        // 1. Detect and declare missing variables from all settings (including cases)
        var detectedVariables = new HashSet<string>();
        foreach (var setting in Settings.Values)
        {
            AddDetectedVariables(setting, detectedVariables);
        }
        foreach (var conditionalCase in ConditionalCases)
        {
            AddDetectedVariables(conditionalCase.Value, detectedVariables);
            foreach (var key in conditionalCase.Keys)
            {
                AddDetectedVariables(key.Left, detectedVariables);
                AddDetectedVariables(key.Right, detectedVariables);
            }
        }

        foreach (var missingVar in VariableDetector.GetMissingVariables(detectedVariables, definedVariables))
        {
            writer.WriteLine($"dynamic {missingVar} = RuriLib.Models.NullDynamic.Instance;");
            definedVariables.Add(missingVar);
        }

        // 2. Prepare the method call
        var autoDescriptor = (AutoBlockDescriptor)Descriptor;
        var parameters = new List<string> { "data" }
            .Concat(Settings.Values.Select(setting =>
            {
                var targetType = autoDescriptor.OriginalParameterTypes.TryGetValue(setting.Name, out var type) ? type : null;
                return CSharpWriter.FromSetting(setting, targetType);
            }));

        var methodCall = $"{Descriptor.Id}({string.Join(", ", parameters)})";
        if (autoDescriptor.Async)
        {
            methodCall = $"await {methodCall}.ConfigureAwait(false)";
        }

        // 3. Generate the atomic assignment logic in a block scope
        writer.WriteLine("{");
        
        if (Safe)
        {
            writer.WriteLine("    string __compValue = string.Empty;");
            writer.WriteLine("    try {");
            writer.WriteLine($"        __compValue = {methodCall};");
            writer.WriteLine("    } catch (Exception safeException) {");
            writer.WriteLine("        data.ERROR = safeException.PrettyPrint();");
            writer.WriteLine("        data.Logger.Log($\"[SAFE MODE] Exception caught and saved to data.ERROR: {data.ERROR}\", LogColors.Tomato);");
            writer.WriteLine("    }");
        }
        else
        {
            writer.WriteLine($"    string __compValue = {methodCall};");
        }

        // Apply overrides to __compValue
        var first = true;
        for (var i = 0; i < ConditionalCases.Count; i++)
        {
            var conditionalCase = ConditionalCases[i];
            var conditionExpression = BuildConditionExpression(conditionalCase);
            
            if (conditionalCase.Keys.Count == 0)
                writer.WriteLine(first ? "    if (true)" : "    else");
            else
                writer.WriteLine(first ? $"    if ({conditionExpression})" : $"    else if ({conditionExpression})");

            writer.WriteLine("    {");
            writer.WriteLine($"        __compValue = {CSharpWriter.FromSetting(conditionalCase.Value)};");
            writer.WriteLine("        data.Logger.LogHeader();");
            writer.WriteLine($"        data.Logger.Log($\"Conditional constant '{conditionalCase.Name.Replace("\"", "\\\"")}' matched. Set {OutputVariable} = {{__compValue}}\", LogColors.YellowGreen);");
            writer.WriteLine("    }");
            first = false;
        }

        // Final assignment to the real output variable
        if (definedVariables.Contains(OutputVariable) || OutputVariable.StartsWith("globals."))
        {
            writer.WriteLine($"    {OutputVariable} = __compValue;");
        }
        else
        {
            writer.WriteLine($"    string {OutputVariable} = __compValue;");
            definedVariables.Add(OutputVariable);
        }

        writer.WriteLine($"    data.LogVariableAssignment(nameof({OutputVariable}));");
        if (IsCapture)
        {
            writer.WriteLine($"    data.MarkForCapture(nameof({OutputVariable}));");
        }

        writer.WriteLine("}");

        return writer.ToString();
    }

    private void AddDetectedVariables(BlockSetting setting, HashSet<string> detectedVariables)
    {
        if (setting.InterpolatedSetting != null)
        {
            switch (setting.InterpolatedSetting)
            {
                case InterpolatedStringSetting str:
                    detectedVariables.UnionWith(VariableDetector.DetectFromInterpolatedString(str.Value));
                    break;
                case InterpolatedListOfStringsSetting list:
                    foreach (var item in list.Value)
                        detectedVariables.UnionWith(VariableDetector.DetectFromInterpolatedString(item));
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
        if (setting.InputMode == SettingInputMode.Variable && !string.IsNullOrEmpty(setting.InputVariableName))
        {
            var baseVar = VariableDetector.ExtractBaseVariableName(setting.InputVariableName);
            if (!string.IsNullOrEmpty(baseVar)) detectedVariables.Add(baseVar);
        }
    }

    private string ExtractCases(string script, int startingLineNumber)
    {
        ConditionalCases.Clear();
        using var reader = new StringReader(script);
        using var writer = new StringWriter();

        var currentLineNumber = startingLineNumber;
        ConditionalConstantStringCase currentCase = null;

        string line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("CASE", StringComparison.OrdinalIgnoreCase))
            {
                var content = trimmed;
                try
                {
                    LineParser.ParseToken(ref content); // Consume CASE
                    var name = LineParser.ParseLiteral(ref content);
                    var modeToken = LineParser.ParseToken(ref content);
                    var mode = Enum.Parse<KeychainMode>(modeToken);

                    currentCase = new ConditionalConstantStringCase
                    {
                        Name = name,
                        Mode = mode
                    };
                    ConditionalCases.Add(currentCase);
                }
                catch (Exception)
                {
                    throw new LoliCodeParsingException(currentLineNumber, $"Invalid CASE declaration: {trimmed}");
                }

                writer.WriteLine();
            }
            else if (trimmed.StartsWith("ENDCASE", StringComparison.OrdinalIgnoreCase))
            {
                if (currentCase == null)
                {
                    throw new LoliCodeParsingException(currentLineNumber, "ENDCASE encountered without a matching CASE.");
                }

                currentCase = null;
                writer.WriteLine();
            }
            else if (currentCase != null && (trimmed.StartsWith(caseValueParameter.Name, StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Value", StringComparison.OrdinalIgnoreCase)))
            {
                var valueLine = trimmed;

                try
                {
                    LineParser.ParseToken(ref valueLine); // Consume the name (conditionalValue or Value)
                    valueLine = valueLine.TrimStart();
                    if (!valueLine.StartsWith('='))
                    {
                        throw new Exception("Missing =");
                    }

                    valueLine = valueLine[1..].TrimStart();
                    
                    // Explicitly handle interpolated mode here just in case ParseSettingValue has issues with currentCase.Value
                    if (valueLine.StartsWith('$'))
                    {
                        valueLine = valueLine[1..].TrimStart();
                        currentCase.Value.InputMode = SettingInputMode.Interpolated;
                        if (currentCase.Value.InterpolatedSetting is InterpolatedStringSetting interpStr)
                        {
                            interpStr.Value = LineParser.ParseLiteral(ref valueLine);
                        }
                        
                        // Sync fixed setting too
                        if (currentCase.Value.FixedSetting is StringSetting fixedStr)
                        {
                            fixedStr.Value = (currentCase.Value.InterpolatedSetting as InterpolatedStringSetting).Value;
                        }
                    }
                    else if (valueLine.StartsWith('@'))
                    {
                        valueLine = valueLine[1..].TrimStart();
                        currentCase.Value.InputMode = SettingInputMode.Variable;
                        currentCase.Value.InputVariableName = LineParser.ParseToken(ref valueLine);
                    }
                    else
                    {
                        currentCase.Value.InputMode = SettingInputMode.Fixed;
                        if (currentCase.Value.FixedSetting is StringSetting fixedStr)
                        {
                            fixedStr.Value = LineParser.ParseLiteral(ref valueLine);
                        }
                    }
                }
                catch (Exception)
                {
                    throw new LoliCodeParsingException(currentLineNumber, $"Invalid conditional value declaration: {trimmed}");
                }

                writer.WriteLine();
            }
            else if (currentCase != null && keyRegex.IsMatch(trimmed))
            {
                var keyLine = trimmed;

                try
                {
                    var keyType = LineParser.ParseToken(ref keyLine);
                    var key = LoliCodeParser.ParseKey(ref keyLine, keyType);
                    currentCase.Keys.Add(key);
                }
                catch (Exception)
                {
                    throw new LoliCodeParsingException(currentLineNumber, $"Invalid conditional key: {trimmed}");
                }

                writer.WriteLine();
            }
            else
            {
                writer.WriteLine(line);
            }

            currentLineNumber++;
        }

        if (currentCase != null)
        {
            throw new LoliCodeParsingException(currentLineNumber, "CASE block was not closed with ENDCASE.");
        }

        return writer.ToString();
    }

    private static string BuildConditionExpression(ConditionalConstantStringCase conditionalCase)
    {
        if (conditionalCase.Keys.Count == 0)
        {
            return "true";
        }

        var renderedKeys = conditionalCase.Keys
            .Select(CSharpWriter.ConvertKey)
            .ToList();

        return conditionalCase.Mode switch
        {
            KeychainMode.OR => string.Join(" || ", renderedKeys),
            KeychainMode.AND => string.Join(" && ", renderedKeys),
            _ => throw new Exception("Invalid keychain mode")
        };
    }
}
