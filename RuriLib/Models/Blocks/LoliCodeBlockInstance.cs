using RuriLib.Extensions;
using RuriLib.Helpers;
using RuriLib.Helpers.CSharp;
using RuriLib.Helpers.LoliCode;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Blocks.Settings.Interpolated;
using RuriLib.Models.Configs;
using RuriLib.Models.Proxies;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RuriLib.Helpers.Transpilers;

namespace RuriLib.Models.Blocks;

public partial class LoliCodeBlockInstance(LoliCodeBlockDescriptor descriptor) : BlockInstance(descriptor)
{
    private readonly string _validTokenRegex = "[A-Za-z][A-Za-z0-9_]*";
    public string Script { get; set; }
    public int StartingLineNumber { get; set; }

    public override string ToLC(bool printDefaultParams = false) => Script;

    public override void FromLC(ref string script, ref int lineNumber)
    {
        Script = script;
        StartingLineNumber = lineNumber;
        lineNumber += script.CountLines();
    }

    public override string ToCSharp(List<string> definedVariables, ConfigSettings settings)
    {
        using var reader = new StringReader(Script);
        using var writer = new StringWriter();
        string line, trimmedLine;
        var relativeLineNumber = 0;

        // First pass: detect all missing variables from the entire script
        var detectedVariables = new HashSet<string>();
        using var firstPassReader = new StringReader(Script);
        while ((line = firstPassReader.ReadLine()) != null)
        {
            trimmedLine = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith("//"))
            {
                // Only detect variables from actual LoliCode statements, not from block definitions
                if (IsLoliCodeStatement(trimmedLine))
                {
                    detectedVariables.UnionWith(VariableDetector.DetectFromLoliCodeStatement(trimmedLine));
                }

                // Special case: output variable declaration lines ("=> VAR @name")
                var varDeclMatch = MyRegex().Match(trimmedLine);
                if (varDeclMatch.Success)
                {
                    _ = detectedVariables.Add(varDeclMatch.Groups[1].Value);
                }
            }
        }

        // Also detect variables from IF/WHILE conditions with keys
        var keyVariables = DetectVariablesFromKeys();
        detectedVariables.UnionWith(keyVariables);

        // Emit NullDynamic declarations for ALL detected variables (except special prefixes)
        foreach (var varName in detectedVariables.Where(static v => !v.StartsWith("input.") && !v.StartsWith("globals.") && !v.StartsWith("data.")))
        {
            if (!definedVariables.Contains(varName))
            {
                writer.WriteLine($"dynamic {varName} = RuriLib.Models.NullDynamic.Instance;");
                definedVariables.Add(varName);
            }
        }

        // Second pass: transpile the script
        while ((line = reader.ReadLine()) != null)
        {
            relativeLineNumber++;
            var absoluteLineNumber = StartingLineNumber + relativeLineNumber - 1;
            trimmedLine = line.Trim();

            // Look for @variable tokens and auto-declare them if missing
            foreach (Match m in MyRegex1().Matches(trimmedLine))
            {
                var varName = m.Groups[1].Value;
                if (!definedVariables.Contains(varName) && varName is not ("input" or "globals" or "data"))
                {
                    writer.WriteLine($"dynamic {varName} = RuriLib.Models.NullDynamic.Instance;");
                    definedVariables.Add(varName);
                }
            }

            // Skip empty lines or comments
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("//"))
            {
                writer.WriteLine(line);
                continue;
            }

            // Add line number comment for debugging
            writer.WriteLine($"// LoliCode line {absoluteLineNumber}: {trimmedLine.Replace("*/", "*//")}");

            // Try to read it as a LoliCode-exclusive statement
            try
            {
                writer.WriteLine(TranspileStatement(trimmedLine, definedVariables, settings));
            }

            // If it failed, we assume what is written is bare C# so we just copy it over (untrimmed)
            catch (NotSupportedException)
            {
                writer.WriteLine(line);
            }
        }

        return writer.ToString();
    }

    private string TranspileStatement(string input, List<string> definedVariables, ConfigSettings settings)
    {
        // Use a dummy dictionary because we don't need to track labels here (they are handled by Stack2CSharpTranspiler pre-pass)
        var dummyLabels = new Dictionary<string, int>();
        return LoliCodeStatementTranspiler.TranspileStatement(input, definedVariables, settings, ref dummyLabels);
    }

    /// <summary>
    /// Determines if a line is a LoliCode statement vs a block definition or other content.
    /// </summary>
    private static bool IsLoliCodeStatement(string line)
    {
        // Skip block definitions and other non-LoliCode content
        if (line.StartsWith("BLOCK:") ||
            line.StartsWith("LABEL:") ||
            line.StartsWith("ENDBLOCK") ||
            line.StartsWith("TYPE:") ||
            line.StartsWith("CONTENT:") ||
            line.StartsWith("SAFE") ||
            (line.Contains('=') && !line.StartsWith("IF ") && !line.StartsWith("WHILE ") && !line.StartsWith("SET ")) ||
            line.StartsWith("url =") ||
            line.StartsWith("method =") ||
            line.StartsWith("value =") ||
            line.Contains("\"application/"))
        {
            return false;
        }

        // These are LoliCode statements
        return line.StartsWith("IF ") ||
               line.StartsWith("WHILE ") ||
               line.StartsWith("ELSE") ||
               line.StartsWith("END") ||
               line.StartsWith("JUMP ") ||
               line.StartsWith("LOG ") ||
               line.StartsWith("CLOG ") ||
               line.StartsWith("SET ") ||
               line.StartsWith("MARK ") ||
               line.StartsWith("UNMARK ") ||
               line.StartsWith("REPEAT ") ||
               line.StartsWith("FOREACH ") ||
               line.StartsWith("LOCK ") ||
               line.StartsWith("ACQUIRELOCK ") ||
               line.StartsWith("RELEASELOCK ") ||
               line.StartsWith("TAKEONE ") ||
               line.StartsWith("TAKE ") ||
               line.StartsWith('#') ||
               line.StartsWith('>');
    }

    private HashSet<string> DetectVariablesFromKeys()
    {
        var detected = new HashSet<string>();

        using var reader = new StringReader(Script);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("//")) continue;

            // Match WHILE and IF lines
            Match match;
            if ((match = MyRegex9().Match(trimmedLine)).Success || (match = MyRegex16().Match(trimmedLine)).Success)
            {
                var condition = match.Groups[1].Value.Trim();
                if (LoliCodeParser.keyIdentifiers.Any(t => condition.StartsWith(t)))
                {
                    try
                    {
                        var keyType = LineParser.ParseToken(ref condition);
                        var key = LoliCodeParser.ParseKey(ref condition, keyType);

                        // Detect variables from interpolated string settings inside keys
                        if (key.Left.InterpolatedSetting is InterpolatedStringSetting leftStr)
                        {
                            detected.UnionWith(VariableDetector.DetectFromInterpolatedString(leftStr.Value));
                        }
                        if (key.Right.InterpolatedSetting is InterpolatedStringSetting rightStr)
                        {
                            detected.UnionWith(VariableDetector.DetectFromInterpolatedString(rightStr.Value));
                        }

                        // Detect variables from variable input modes inside keys
                        if (key.Left.InputMode == SettingInputMode.Variable && !string.IsNullOrEmpty(key.Left.InputVariableName))
                        {
                            var baseVar = VariableDetector.ExtractBaseVariableName(key.Left.InputVariableName);
                            if (!string.IsNullOrEmpty(baseVar))
                            {
                                detected.Add(baseVar);
                            }
                        }
                        if (key.Right.InputMode == SettingInputMode.Variable && !string.IsNullOrEmpty(key.Right.InputVariableName))
                        {
                            var baseVar = VariableDetector.ExtractBaseVariableName(key.Right.InputVariableName);
                            if (!string.IsNullOrEmpty(baseVar))
                            {
                                detected.Add(baseVar);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore parsing errors in detection
                    }
                }
            }
        }

        return detected;
    }

    [GeneratedRegex(@"^=> VAR @?([A-Za-z][A-Za-z0-9_]*)")]
    private static partial Regex MyRegex();
    
    [GeneratedRegex("@([A-Za-z][A-Za-z0-9_]*)")]
    private static partial Regex MyRegex1();
    
    [GeneratedRegex("^WHILE (.+)$")]
    private static partial Regex MyRegex9();
    
    [GeneratedRegex("^IF (.+)$")]
    private static partial Regex MyRegex16();
}
