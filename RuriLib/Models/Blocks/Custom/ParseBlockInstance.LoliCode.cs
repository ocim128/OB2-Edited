using RuriLib.Exceptions;
using RuriLib.Extensions;
using RuriLib.Helpers.LoliCode;
using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Blocks.Custom.Parse;
using RuriLib.Models.Blocks.Settings;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace RuriLib.Models.Blocks.Custom
{
    public partial class ParseBlockInstance
    {
        public override string ToLC(bool printDefaultParams = false)
        {
            using var writer = new LoliCodeWriter(base.ToLC(printDefaultParams));

            if (Safe)
            {
                writer.AppendLine("SAFE", 2);
            }

            if (Recursive)
            {
                writer.AppendLine("RECURSIVE", 2);
            }

            writer.AppendLine($"MODE:{Mode}", 2);

            var isCap = IsCapture ? "CAP" : "VAR";
            writer.AppendLine($"=> {isCap} @{OutputVariable}", 2);

            AppendConditionalCases(writer, printDefaultParams);

            return writer.ToString();
        }

        public override void FromLC(ref string script, ref int lineNumber)
        {
            var sanitizedScript = ExtractCases(script, lineNumber);
            script = sanitizedScript;

            base.FromLC(ref script, ref lineNumber);

            using var reader = new StringReader(script);

            while (reader.ReadLine() is { } line)
            {
                line = line.Trim();
                lineNumber++;
                var lineCopy = line;

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("SAFE"))
                {
                    Safe = true;
                    continue;
                }

                if (line.StartsWith("RECURSIVE"))
                {
                    Recursive = true;
                }
                else if (line.StartsWith("MODE"))
                {
                    try
                    {
                        Mode = Enum.Parse<ParseMode>(Regex.Match(line, "MODE:([A-Za-z]+)").Groups[1].Value);
                    }
                    catch
                    {
                        throw new LoliCodeParsingException(lineNumber, $"Could not understand the parsing mode: {lineCopy.TruncatePretty(50)}");
                    }
                }
                else if (line.StartsWith("=>"))
                {
                    try
                    {
                        var match = Regex.Match(line, "^=> ([A-Za-z]{3}) (.*)$");
                        IsCapture = match.Groups[1].Value.Equals("CAP", StringComparison.OrdinalIgnoreCase);
                        OutputVariable = match.Groups[2].Value.Trim()[1..];
                    }
                    catch
                    {
                        throw new LoliCodeParsingException(lineNumber, $"The output variable declaration is in the wrong format: {lineCopy.TruncatePretty(50)}");
                    }
                }
                else
                {
                    try
                    {
                        LoliCodeParser.ParseSetting(ref line, Settings, Descriptor);
                    }
                    catch
                    {
                        throw new LoliCodeParsingException(lineNumber, $"Could not parse the setting: {lineCopy.TruncatePretty(50)}");
                    }
                }
            }
        }

        private void AppendConditionalCases(LoliCodeWriter writer, bool printDefaults)
        {
            foreach (var conditionalCase in ConditionalCases)
            {
                var nameSetting = BlockSettingFactory.CreateStringSetting("caseName", conditionalCase.Name);
                writer
                    .AppendToken("CASE", 2)
                    .AppendToken(LoliCodeWriter.GetSettingValue(nameSetting))
                    .AppendLine(conditionalCase.Mode.ToString());

                writer
                    .AppendToken(CaseModeToken, 4)
                    .AppendLine(conditionalCase.OverrideMode.ToString());

                foreach (var settingName in caseSettingNames)
                {
                    if (!conditionalCase.Settings.TryGetValue(settingName, out var setting) ||
                        !Descriptor.Parameters.TryGetValue(settingName, out var parameter))
                    {
                        continue;
                    }

                    writer.AppendSetting(setting, parameter, 4, printDefaults);
                }

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
        }

        private string ExtractCases(string script, int startingLineNumber)
        {
            ConditionalCases.Clear();
            using var reader = new StringReader(script);
            using var writer = new StringWriter();

            var currentLineNumber = startingLineNumber;
            ParseConditionalCase currentCase = null;

            string line;
            while ((line = reader.ReadLine()) is not null)
            {
                var trimmed = line.Trim();

                if (Regex.IsMatch(trimmed, @"^CASE(\s|$)", RegexOptions.IgnoreCase))
                {
                    var content = trimmed;
                    try
                    {
                        LineParser.ParseToken(ref content);
                        var name = LineParser.ParseLiteral(ref content);
                        var modeToken = LineParser.ParseToken(ref content);
                        var mode = Enum.Parse<KeychainMode>(modeToken);

                        currentCase = CreateConditionalCase();
                        currentCase.Name = name;
                        currentCase.Mode = mode;
                        ConditionalCases.Add(currentCase);
                    }
                    catch
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
                else if (currentCase != null && trimmed.StartsWith(CaseModeToken, StringComparison.OrdinalIgnoreCase))
                {
                    var content = trimmed;
                    try
                    {
                        LineParser.ParseToken(ref content);
                        var modeToken = LineParser.ParseToken(ref content);
                        currentCase.OverrideMode = Enum.Parse<ParseMode>(modeToken);
                    }
                    catch
                    {
                        throw new LoliCodeParsingException(currentLineNumber, $"Invalid conditional parse mode: {trimmed}");
                    }

                    writer.WriteLine();
                }
                else if (currentCase != null && TryParseConditionalSetting(trimmed, currentCase))
                {
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
                    catch
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

        private bool TryParseConditionalSetting(string line, ParseConditionalCase currentCase)
        {
            foreach (var name in caseSettingNames)
            {
                if (!line.StartsWith($"{name} ", StringComparison.Ordinal) &&
                    !line.StartsWith($"{name}\t", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Descriptor.Parameters.ContainsKey(name) ||
                    !currentCase.Settings.ContainsKey(name))
                {
                    return false;
                }

                var temp = line;
                LoliCodeParser.ParseSetting(ref temp, currentCase.Settings, Descriptor);
                return true;
            }

            return false;
        }
    }
}
