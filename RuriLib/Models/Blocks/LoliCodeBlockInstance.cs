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
        Match match;

        // (RESOURCES) TAKEONE
        // TAKEONE FROM "MyResource" => "myString"
        if ((match = MyRegex2().Match(input)).Success)
        {
            if (definedVariables.Contains(match.Groups[2].Value))
            {
                return $"{match.Groups[2].Value} = globals.Resources[{match.Groups[1].Value}].TakeOne();";
            }

            definedVariables.Add(match.Groups[2].Value);
            return $"string {match.Groups[2].Value} = globals.Resources[{match.Groups[1].Value}].TakeOne();";
        }

        // (RESOURCES) TAKE
        // TAKE 5 FROM "MyResource" => "myList"
        if ((match = MyRegex3().Match(input)).Success)
        {
            if (definedVariables.Contains(match.Groups[3].Value))
            {
                return $"{match.Groups[3].Value} = globals.Resources[{match.Groups[2].Value}].Take({match.Groups[1].Value});";
            }

            definedVariables.Add(match.Groups[3].Value);
            return $"List<string> {match.Groups[3].Value} = globals.Resources[{match.Groups[2].Value}].Take({match.Groups[1].Value});";
        }

        // CODE LABEL
        // #MYLABEL => MYLABEL: ;
        if ((match = Regex.Match(input, $"^#({_validTokenRegex})$")).Success)
        {
            var label = match.Groups[1].Value;
            return $"{label}: ;";
        }

        // JUMP
        // JUMP #MYLABEL => data.Logger.Log("Jumping to label MYLABEL", LogColors.White); goto MYLABEL;
        if ((match = Regex.Match(input, $"^JUMP #({_validTokenRegex})$")).Success)
        {
            var label = match.Groups[1].Value;
            var maxJumps = settings?.GeneralSettings?.MaxJumpIterations > 0 ? settings.GeneralSettings.MaxJumpIterations : 40;
            return $"data.Logger.Log(\"Jumping to label {label}\", LogColors.White);{System.Environment.NewLine}if (++__jumpCount_{label} > {maxJumps}) throw new InvalidOperationException($\"Infinite loop detected at label {label} - maximum {maxJumps} iterations reached\");{System.Environment.NewLine}goto {label};";
        }

        // END
        // END => }
        if (input == "END")
        {
            return "}";
        }

        // REPEAT
        // REPEAT 10 => for (int xyz = 0; xyz < 10; xyz++) {
        if ((match = MyRegex5().Match(input)).Success)
        {
            var i = VariableNames.RandomName();
            return $"for (var {i} = 0; {i} < ({match.Groups[1].Value}).AsInt(); {i}++){System.Environment.NewLine}{{";
        }

        // FOREACH
        // FOREACH v IN list => foreach (var v in list) {
        if ((match = Regex.Match(input, $"^FOREACH ({_validTokenRegex}) IN ({_validTokenRegex})$")).Success)
        {
            return $"foreach (var {match.Groups[1].Value} in {match.Groups[2].Value}){System.Environment.NewLine}{{";
        }

        // LOG
        // LOG myVar => data.Logger.Log(myVar);
        if ((match = MyRegex13().Match(input)).Success)
        {
            return $"data.Logger.LogObject({match.Groups[1].Value});";
        }

        // CLOG
        // CLOG Tomato "hello" => data.Logger.Log("hello", LogColors.Tomato);
        if ((match = MyRegex4().Match(input)).Success)
        {
            return $"data.Logger.LogObject({match.Groups[2].Value}, LogColors.{match.Groups[1].Value});";
        }

        // WHILE
        // WHILE a < b => while (a < b) {
        if ((match = MyRegex6().Match(input)).Success)
        {
            var line = match.Groups[1].Value.Trim();
            if (LoliCodeParser.keyIdentifiers.Any(t => line.StartsWith(t)))
            {
                var keyType = LineParser.ParseToken(ref line);
                var key = LoliCodeParser.ParseKey(ref line, keyType);
                return $"while ({CSharpWriter.ConvertKey(key)}){System.Environment.NewLine}{{";
            }

            return $"while ({line}){System.Environment.NewLine}{{";
        }

        // IF
        // IF a < b => if (a < b) {
        if ((match = MyRegex12().Match(input)).Success)
        {
            var line = match.Groups[1].Value.Trim();
            if (LoliCodeParser.keyIdentifiers.Any(t => line.StartsWith(t)))
            {
                var keyType = LineParser.ParseToken(ref line);
                var key = LoliCodeParser.ParseKey(ref line, keyType);
                return $"if ({CSharpWriter.ConvertKey(key)}){System.Environment.NewLine}{{";
            }

            return $"if ({line}){System.Environment.NewLine}{{";
        }

        // ELSE
        // ELSE => } else {
        if (input == "ELSE")
        {
            return $"}}{System.Environment.NewLine}else{System.Environment.NewLine}{{";
        }

        // ELSE IF
        // ELSE IF a < b => } else if (a < b) {
        if ((match = MyRegex7().Match(input)).Success)
        {
            var line = match.Groups[1].Value.Trim();
            if (LoliCodeParser.keyIdentifiers.Any(t => line.StartsWith(t)))
            {
                var keyType = LineParser.ParseToken(ref line);
                var key = LoliCodeParser.ParseKey(ref line, keyType);
                return $"}}{System.Environment.NewLine}else if ({CSharpWriter.ConvertKey(key)}){System.Environment.NewLine}{{";
            }

            return $"}}{System.Environment.NewLine}else if ({line}){System.Environment.NewLine}{{";
        }

        // SET
        // SET X = myVariable => var X = myVariable;
        if (input.StartsWith("SET "))
        {
            try
            {
                var setMatch = Regex.Match(input, $"^SET @?({_validTokenRegex})\\s*=\\s*(.+)$");
                if (!setMatch.Success)
                {
                    throw new FormatException();
                }

                var varName = setMatch.Groups[1].Value;
                var expression = setMatch.Groups[2].Value;

                if (definedVariables.Contains(varName))
                {
                    return $"{varName} = {expression};";
                }

                definedVariables.Add(varName);
                return $"var {varName} = {expression};";
            }
            catch (FormatException)
            {
                throw new NotSupportedException();
            }
        }

        // MARK, UNMARK
        if (input.StartsWith("MARK "))
        {
            return $"data.Mark({input[5..]});";
        }
        if (input.StartsWith("UNMARK "))
        {
            return $"data.Unmark({input[7..]});";
        }

        // USE PROXY
        if (input.StartsWith("SET USEPROXY "))
        {
            var proxyType = ProxyType.Http;
            var useProxy = false;

            if ((match = MyRegex10().Match(input)).Success)
            {
                useProxy = bool.Parse(match.Groups[1].Value);
            }
            else if ((match = Regex.Match(input, $"^SET USEPROXY ({_validTokenRegex})$"))
                .Success)
            {
                var v = match.Groups[1].Value;
                return $"if ({v}){{data.UseProxy = true;}}else{{data.UseProxy = false;}}";
            }

            if (!useProxy)
            {
                return $"data.UseProxy = false;";
            }

            var path = $"data.Providers.Proxies.{proxyType}Proxies";
            return $"if (globals.UseProxies){{data.UseProxy = true;data.Proxy = {path}.GetRandomValid(data.UseBanLoop, data.ProxyGroup);data.Logger.Log(\"Using proxy: \", LogColors.Tomato);data.Logger.LogObject(data.Proxy, LogColors.White);}}else{{data.UseProxy = false;}}";
        }

        // ACQUIRE LOCK
        if ((match = MyRegex14().Match(input)).Success)
        {
            return $"await data.Locker.Acquire({match.Groups[1].Value}, data.CancellationToken);";
        }

        // LOCK
        if ((match = MyRegex15().Match(input)).Success)
        {
            return $"await data.Locker.Lock({match.Groups[1].Value}, data.CancellationToken);";
        }

        // RELEASE LOCK
        if ((match = MyRegex11().Match(input)).Success)
        {
            return $"data.Locker.Release({match.Groups[1].Value});";
        }

        throw new NotSupportedException();
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
    [GeneratedRegex("TAKEONE FROM (\"[^\"]+\") => @?\"?([^\"]+)\"?")]
    private static partial Regex MyRegex2();
    [GeneratedRegex("TAKE ([0-9]+) FROM (\"[^\"]+\") => @?\"?([^\"]+)\"?")]
    private static partial Regex MyRegex3();
    [GeneratedRegex("^CLOG ([A-Za-z]+) (.+)$")]
    private static partial Regex MyRegex4();
    [GeneratedRegex("^REPEAT (.+)$")]
    private static partial Regex MyRegex5();
    [GeneratedRegex("^WHILE (.+)$")]
    private static partial Regex MyRegex6();
    [GeneratedRegex("ELSE IF (.+)$")]
    private static partial Regex MyRegex7();
    [GeneratedRegex("^ELSE IF (.+)$")]
    private static partial Regex MyRegex8();
    [GeneratedRegex("^WHILE (.+)$")]
    private static partial Regex MyRegex9();
    [GeneratedRegex("^SET USEPROXY (TRUE|FALSE)$")]
    private static partial Regex MyRegex10();
    [GeneratedRegex("^RELEASELOCK (.+)$")]
    private static partial Regex MyRegex11();
    [GeneratedRegex("^IF (.+)$")]
    private static partial Regex MyRegex12();
    [GeneratedRegex("^LOG (.+)$")]
    private static partial Regex MyRegex13();
    [GeneratedRegex("^ACQUIRELOCK (.+)$")]
    private static partial Regex MyRegex14();
    [GeneratedRegex("^LOCK (.+)$")]
    private static partial Regex MyRegex15();
    [GeneratedRegex("^IF (.+)$")]
    private static partial Regex MyRegex16();
}
