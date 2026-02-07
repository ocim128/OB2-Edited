using RuriLib.Helpers.CSharp;
using RuriLib.Helpers.LoliCode;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Blocks.Settings.Interpolated;
using RuriLib.Models.Configs;
using RuriLib.Models.Proxies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RuriLib.Helpers.Transpilers
{
    public static partial class LoliCodeStatementTranspiler
    {
        private static readonly string _validTokenRegex = "[A-Za-z][A-Za-z0-9_]*";

        public static string TranspileStatement(string input, List<string> definedVariables, ConfigSettings settings, ref Dictionary<string, int> jumpLabels)
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
                // Register label for jump counting
                if (!jumpLabels.ContainsKey(label)) jumpLabels[label] = 0;
                return $"{label}: ;";
            }

            // JUMP
            // JUMP #MYLABEL => data.Logger.Log("Jumping to label MYLABEL", LogColors.White); goto MYLABEL;
            if ((match = Regex.Match(input, $"^JUMP #({_validTokenRegex})$")).Success)
            {
                var label = match.Groups[1].Value;
                var maxJumps = settings?.GeneralSettings?.MaxJumpIterations > 0 ? settings.GeneralSettings.MaxJumpIterations : 40;
                // Note: We assume __jumpCount_{label} is declared at start of script
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
                var i = GetRepeatLoopVariableName(definedVariables);
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

        private static string GetRepeatLoopVariableName(List<string> definedVariables)
        {
            const string prefix = "__ob2_repeat_i";
            var index = 0;

            while (true)
            {
                var candidate = $"{prefix}{index++}";
                if (definedVariables.Contains(candidate))
                {
                    continue;
                }

                // Reserve to keep deterministic uniqueness across the generated script.
                definedVariables.Add(candidate);
                return candidate;
            }
        }
    }
}
