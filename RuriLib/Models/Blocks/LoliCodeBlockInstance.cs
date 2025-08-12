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
                writer.WriteLine(TranspileStatement(trimmedLine, definedVariables));
            }

            // If it failed, we assume what is written is bare C# so we just copy it over (untrimmed)
            catch (NotSupportedException)
            {
                writer.WriteLine(line);
            }
        }

        return writer.ToString();
    }

    private string TranspileStatement(string input, List<string> definedVariables)
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
            return $"data.Logger.Log(\"Jumping to label {label}\", LogColors.White);{System.Environment.NewLine}if (++__jumpCount_{label} > 30) throw new InvalidOperationException($\"Infinite loop detected at label {label} - maximum 30 iterations reached\");{System.Environment.NewLine}goto {label};";
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

        // TRY
        // TRY => try {
        if (input == "TRY")
        {
            return $"try{System.Environment.NewLine}{{";
        }

        // CATCH
        // CATCH => } catch {
        if (input == "CATCH")
        {
            return $"}}{System.Environment.NewLine}catch{System.Environment.NewLine}{{";
        }

        // FINALLY
        // FINALLY => } finally {
        if (input == "FINALLY")
        {
            return $"}}{System.Environment.NewLine}finally{System.Environment.NewLine}{{";
        }

        // LOCK
        // LOCK globals => lock (globals) {
        if ((match = MyRegex15().Match(input)).Success)
        {
            return $"lock({match.Groups[1].Value}){System.Environment.NewLine}{{";
        }

        // ACQUIRELOCK
        // ACQUIRELOCK globals => await data.AsyncLocker.Acquire(nameof(globals), data.CancellationToken);
        if ((match = MyRegex14().Match(input)).Success)
        {
            return $"await data.AsyncLocker.Acquire(nameof({match.Groups[1].Value}), data.CancellationToken);";
        }

        // RELEASELOCK
        // RELEASELOCK globals => data.AsyncLocker.Release(nameof(globals));
        if ((match = MyRegex11().Match(input)).Success)
        {
            return $"data.AsyncLocker.Release(nameof({match.Groups[1].Value}));";
        }

        // SET VAR
        // SET VAR myString "hello" => string myString = "hello";
        if ((match = Regex.Match(input, $"^SET VAR @?\"?({_validTokenRegex})\"? (.+)$")).Success)
        {
            if (definedVariables.Contains(match.Groups[1].Value))
            {
                return $"{match.Groups[1].Value} = {match.Groups[2].Value};";
            }

            definedVariables.Add(match.Groups[1].Value);
            return $"string {match.Groups[1].Value} = {match.Groups[2].Value};";
        }

        // SET CAP
        // SET CAP myCapture "hello" => string myString = "hello"; data.MarkForCapture(nameof(myCapture));
        if ((match = Regex.Match(input, $"^SET CAP @?\"?({_validTokenRegex})\"? (.+)$")).Success)
        {
            if (definedVariables.Contains(match.Groups[1].Value))
            {
                return $"{match.Groups[1].Value} = {match.Groups[2].Value};{System.Environment.NewLine}data.MarkForCapture(nameof({match.Groups[1].Value}));";
            }

            definedVariables.Add(match.Groups[1].Value);
            return $"string {match.Groups[1].Value} = {match.Groups[2].Value};{System.Environment.NewLine}data.MarkForCapture(nameof({match.Groups[1].Value}));";
        }

        // SET USEPROXY
        // SET USEPROXY TRUE => data.UseProxy = "true";
        if ((match = MyRegex10().Match(input)).Success)
        {
            return $"data.UseProxy = {match.Groups[1].Value.ToLower(System.Globalization.CultureInfo.CurrentCulture)};";
        }

        // SET PROXY
        // SET PROXY "127.0.0.1" 9050 SOCKS5 => data.Proxy = new Proxy("127.0.0.1", 9050, ProxyType.Socks5);
        // SET PROXY "127.0.0.1" 9050 SOCKS5 "username" "password" => data.Proxy = new Proxy("127.0.0.1", 9050, ProxyType.Socks5, "username", "password");
        if (input.StartsWith("SET PROXY "))
        {
            var setProxyParams = input["SET PROXY ".Length..].Split(' ');
            var proxyType = (ProxyType)Enum.Parse(typeof(ProxyType), setProxyParams[2], true);

            return setProxyParams.Length == 3
                ? $"data.Proxy = new Proxy({setProxyParams[0]}, {setProxyParams[1]}, ProxyType.{proxyType});"
                : $"data.Proxy = new Proxy({setProxyParams[0]}, {setProxyParams[1]}, ProxyType.{proxyType}, {setProxyParams[3]}, {setProxyParams[4]});";
        }

        // MARK
        // MARK @myVar => data.MarkForCapture(nameof(myVar));
        if ((match = Regex.Match(input, $"^MARK @?({_validTokenRegex})$")).Success)
        {
            return $"data.MarkForCapture(nameof({match.Groups[1].Value}));";
        }

        // UNMARK
        // UNMARK @myVar => data.MarkedForCapture.Remove(nameof(myVar));
        return (match = Regex.Match(input, $"^UNMARK @?({_validTokenRegex})$")).Success
            ? $"data.UnmarkCapture(nameof({match.Groups[1].Value}));"
            : throw new NotSupportedException();
    }

    /// <summary>
    /// Detects variables from Key objects in IF/WHILE statements.
    /// </summary>
    private HashSet<string> DetectVariablesFromKeys()
    {
        var variables = new HashSet<string>();

        if (string.IsNullOrEmpty(Script))
        {
            return variables;
        }

        using var reader = new StringReader(Script);
        string line;

        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();

            // Check for IF statements with keys
            var ifMatch = MyRegex16().Match(trimmed);
            if (ifMatch.Success)
            {
                var condition = ifMatch.Groups[1].Value.Trim();
                if (LoliCodeParser.keyIdentifiers.Any(condition.StartsWith))
                {
                    variables.UnionWith(ExtractVariablesFromKeyCondition(condition));
                }
            }

            // Check for WHILE statements with keys
            var whileMatch = MyRegex9().Match(trimmed);
            if (whileMatch.Success)
            {
                var condition = whileMatch.Groups[1].Value.Trim();
                if (LoliCodeParser.keyIdentifiers.Any(condition.StartsWith))
                {
                    variables.UnionWith(ExtractVariablesFromKeyCondition(condition));
                }
            }

            // Check for ELSE IF statements with keys
            var elseIfMatch = MyRegex8().Match(trimmed);
            if (elseIfMatch.Success)
            {
                var condition = elseIfMatch.Groups[1].Value.Trim();
                if (LoliCodeParser.keyIdentifiers.Any(condition.StartsWith))
                {
                    variables.UnionWith(ExtractVariablesFromKeyCondition(condition));
                }
            }
        }

        return variables;
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

    /// <summary>
    /// Extracts variable names from a key condition like "STRINGKEY @cp Contains "%""
    /// </summary>
    private static HashSet<string> ExtractVariablesFromKeyCondition(string condition)
    {
        var variables = new HashSet<string>();

        try
        {
            var lineCopy = condition;
            var keyType = LineParser.ParseToken(ref lineCopy);
            var key = LoliCodeParser.ParseKey(ref lineCopy, keyType);

            // Check left side of the condition
            if (key.Left.InputMode == SettingInputMode.Variable && !string.IsNullOrEmpty(key.Left.InputVariableName))
            {
                var baseVar = VariableDetector.ExtractBaseVariableName(key.Left.InputVariableName);
                if (!string.IsNullOrEmpty(baseVar))
                {
                    _ = variables.Add(baseVar);
                }
            }

            // Check right side of the condition 
            if (key.Right.InputMode == SettingInputMode.Variable && !string.IsNullOrEmpty(key.Right.InputVariableName))
            {
                var baseVar = VariableDetector.ExtractBaseVariableName(key.Right.InputVariableName);
                if (!string.IsNullOrEmpty(baseVar))
                {
                    _ = variables.Add(baseVar);
                }
            }

            // Check interpolated strings in left side
            if (key.Left.InputMode == SettingInputMode.Interpolated && key.Left.InterpolatedSetting is InterpolatedStringSetting leftString)
            {
                variables.UnionWith(VariableDetector.DetectFromInterpolatedString(leftString.Value));
            }

            // Check interpolated strings in right side
            if (key.Right.InputMode == SettingInputMode.Interpolated && key.Right.InterpolatedSetting is InterpolatedStringSetting rightString)
            {
                variables.UnionWith(VariableDetector.DetectFromInterpolatedString(rightString.Value));
            }
        }
        catch
        {
            // If parsing fails, fall back to regex detection
            foreach (Match match in MyRegex1().Matches(condition))
            {
                _ = variables.Add(match.Groups[1].Value);
            }
        }

        return variables;
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
