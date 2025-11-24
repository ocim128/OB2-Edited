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
        var baseCode = base.ToCSharp(definedVariables, settings);

        if (!ConditionalCases.Any())
        {
            return baseCode;
        }

        using var writer = new StringWriter();
        writer.Write(baseCode);

        // Append conditional overrides after the standard constant assignment.
        writer.WriteLine();
        writer.WriteLine("{");
        var first = true;

        for (var caseIndex = 0; caseIndex < ConditionalCases.Count; caseIndex++)
        {
            var conditionalCase = ConditionalCases[caseIndex];
            var conditionExpression = BuildConditionExpression(conditionalCase);
            if (conditionalCase.Keys.Count == 0)
            {
                writer.WriteLine(first ? "    if (true)" : "    else");
            }
            else
            {
                writer.WriteLine(first
                    ? $"    if ({conditionExpression})"
                    : $"    else if ({conditionExpression})");
            }

            var tempVarName = $"__conditionalValue{caseIndex}";
            writer.WriteLine("    {");
            writer.WriteLine($"        var {tempVarName} = {CSharpWriter.FromSetting(conditionalCase.Value)};");
            writer.WriteLine($"        {OutputVariable} = {tempVarName};");
            writer.WriteLine("        data.Logger.LogHeader();");
            writer.WriteLine("        data.Logger.Log($\"Conditional constant '{0}' matched. Set {1} = {{{2}}}\", LogColors.YellowGreen);", conditionalCase.Name.Replace("\"", "\\\""), OutputVariable, tempVarName);
            writer.WriteLine($"        data.LogVariableAssignment(nameof({OutputVariable}));");
            writer.WriteLine("    }");
            first = false;
        }

        writer.WriteLine("}");

        return writer.ToString();
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
            else if (currentCase != null && trimmed.StartsWith(caseValueParameter.Name, StringComparison.Ordinal))
            {
                var valueLine = trimmed;

                try
                {
                    LoliCodeParser.ParseSettingValue(ref valueLine, currentCase.Value, caseValueParameter);
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
