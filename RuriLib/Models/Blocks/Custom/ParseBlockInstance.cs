using RuriLib.Exceptions;
using RuriLib.Extensions;
using RuriLib.Helpers;
using RuriLib.Helpers.CSharp;
using RuriLib.Helpers.LoliCode;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Blocks.Custom.Parse;
using RuriLib.Models.Blocks.Parameters;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Blocks.Settings.Interpolated;
using RuriLib.Models.Configs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RuriLib.Models.Blocks.Custom
{
    public class ParseBlockInstance : BlockInstance
    {
        private static readonly Regex keyRegex = new("^[A-Z]+KEY ", RegexOptions.Compiled);
        private const string CaseModeToken = "CASEMODE";
        private static readonly string[] caseSettingNames = new[]
        {
            "prefix",
            "suffix",
            "leftDelim",
            "rightDelim",
            "caseSensitive",
            "cssSelector",
            "attributeName",
            "xPath",
            "jToken",
            "pattern",
            "outputFormat",
            "multiLine"
        };

        private string outputVariable = "parseOutput";
        public string OutputVariable
        {
            get => outputVariable;
            set => outputVariable = VariableNames.MakeValid(value);
        }

        public bool Recursive { get; set; } = false;
        public bool IsCapture { get; set; } = false;
        public bool Safe { get; set; } = false;
        public ParseMode Mode { get; set; } = ParseMode.LR;
        public List<ParseConditionalCase> ConditionalCases { get; } = new();

        public ParseConditionalCase CreateConditionalCase()
        {
            var conditionalCase = new ParseConditionalCase();
            InitializeCaseSettings(conditionalCase);
            return conditionalCase;
        }

        private void InitializeCaseSettings(ParseConditionalCase conditionalCase)
        {
            conditionalCase.OverrideMode = Mode;

            foreach (var name in caseSettingNames)
            {
                if (!Descriptor.Parameters.ContainsKey(name) || !Settings.ContainsKey(name))
                {
                    continue;
                }

                conditionalCase.Settings[name] = CloneSetting(Settings[name], Descriptor.Parameters[name]);
            }
        }

        public ParseBlockInstance(ParseBlockDescriptor descriptor)
            : base(descriptor)
        {
            
        }

        public override string ToLC(bool printDefaultParams = false)
        {
            /*
             *   recursive = True
             *   mode = LR
             *   input = "hello how are you"
             *   leftDelim = "hello"
             *   rightDelim = "you"
             *   caseSensitive = True
             *   => CAP PARSED
             */

            using var writer = new LoliCodeWriter(base.ToLC(printDefaultParams));

            if (Safe)
            {
                writer.AppendLine("SAFE", 2);
            }

            if (Recursive)
                writer.AppendLine("RECURSIVE", 2);

            writer.AppendLine($"MODE:{Mode}", 2);
            
            var isCap = IsCapture ? "CAP" : "VAR";
            writer.AppendLine($"=> {isCap} @{OutputVariable}", 2);

            AppendConditionalCases(writer, printDefaultParams);

            return writer.ToString();
        }

        public override void FromLC(ref string script, ref int lineNumber)
        {
            /*
             *   recursive = True
             *   mode = LR
             *   input = "hello how are you"
             *   leftDelim = "hello"
             *   rightDelim = "you"
             *   caseSensitive = True
             *   => CAP PARSED
             */

            var sanitizedScript = ExtractCases(script, lineNumber);
            script = sanitizedScript;

            // First parse the options that are common to every BlockInstance
            base.FromLC(ref script, ref lineNumber);

            using var reader = new StringReader(script);

            while (reader.ReadLine() is { } line)
            {
                line = line.Trim();
                lineNumber++;
                var lineCopy = line;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("SAFE"))
                {
                    Safe = true;
                    continue;
                }

                if (line.StartsWith("RECURSIVE"))
                    Recursive = true;

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

        public override string ToCSharp(List<string> definedVariables, ConfigSettings settings)
        {
            using var writer = new StringWriter();
            var outputType = Recursive ? "List<string>" : "string";
            var defaultReturnValue = Recursive ? "new List<string>()" : "string.Empty";
            var alreadyDefined = definedVariables.Contains(OutputVariable) || OutputVariable.StartsWith("globals.");

            if (!alreadyDefined)
            {
                if (!Disabled)
                    definedVariables.Add(OutputVariable);

                writer.WriteLine($"{outputType} {OutputVariable} = {defaultReturnValue};");
            }
            else
            {
                writer.WriteLine($"{OutputVariable} = {defaultReturnValue};");
            }

            if (Safe)
            {
                writer.WriteLine("try {");
            }

            if (ConditionalCases.Any())
            {
                writer.WriteLine("var __parseCaseMatched = false;");
                var first = true;

                foreach (var conditionalCase in ConditionalCases)
                {
                    var conditionExpression = BuildConditionExpression(conditionalCase);
                    var prefix = conditionalCase.Keys.Count == 0
                        ? (first ? "if (true)" : "else")
                        : (first ? $"if ({conditionExpression})" : $"else if ({conditionExpression})");

                    writer.WriteLine(prefix);
                    writer.WriteLine("{");
                    writer.WriteLine("    __parseCaseMatched = true;");
                    WriteParseMethod(writer, conditionalCase.OverrideMode, conditionalCase.Settings, 4);
                    writer.WriteLine("    data.Logger.LogHeader();");
                    writer.WriteLine($"    data.Logger.Log($\"Conditional parse '{conditionalCase.Name.Replace("\"", "\\\"")}' matched\", LogColors.YellowGreen);");
                    writer.WriteLine("}");
                    first = false;
                }

                writer.WriteLine("if (!__parseCaseMatched)");
                writer.WriteLine("{");
                WriteParseMethod(writer, null, null, 4);
                writer.WriteLine("}");
            }
            else
            {
                WriteParseMethod(writer);
            }

            if (Safe)
            {
                writer.WriteLine("} catch (Exception safeException) {");
                writer.WriteLine("data.ERROR = safeException.PrettyPrint();");
                writer.WriteLine("data.Logger.Log($\"[SAFE MODE] Exception caught and saved to data.ERROR: {data.ERROR}\", LogColors.Tomato); }");
            }

            return writer.ToString();
        }

        private void WriteParseMethod(StringWriter writer, ParseMode? overrideMode = null, Dictionary<string, BlockSetting> overrideSettings = null, int indent = 0)
        {
            var indentString = new string(' ', indent);
            var modeToUse = overrideMode ?? Mode;
            writer.Write($"{indentString}{OutputVariable} = ");

            switch (modeToUse)
            {
                case ParseMode.LR:
                    writer.Write("ParseBetweenStrings");
                    break;
                case ParseMode.CSS:
                    writer.Write("QueryCssSelector");
                    break;
                case ParseMode.XPath:
                    writer.Write("QueryXPath");
                    break;
                case ParseMode.Json:
                    writer.Write("QueryJsonToken");
                    break;
                case ParseMode.Regex:
                    writer.Write("MatchRegexGroups");
                    break;
            }

            if (Recursive)
                writer.Write("Recursive");

            writer.Write("(data, ");
            writer.Write(CSharpWriter.FromSetting(Settings["input"]) + ", ");

            switch (modeToUse)
            {
                case ParseMode.LR:
                    writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "leftDelim")) + ", ");
                    writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "rightDelim")) + ", ");
                    writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "caseSensitive")) + ", ");
                    break;
                case ParseMode.CSS:
                    writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "cssSelector")) + ", ");
                    writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "attributeName")) + ", ");
                    break;
                case ParseMode.XPath:
                    writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "xPath")) + ", ");
                    writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "attributeName")) + ", ");
                    break;
                case ParseMode.Json:
                    writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "jToken")) + ", ");
                    break;
                case ParseMode.Regex:
                    writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "pattern")) + ", ");
                    writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "outputFormat")) + ", ");
                    writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "multiLine")) + ", ");
                    break;
            }

            writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "prefix")) + ", ");
            writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "suffix")) + ", ");
            writer.Write(CSharpWriter.FromSetting(Settings["urlEncodeOutput"]));
            writer.WriteLine(");");

            writer.WriteLine($"{indentString}data.LogVariableAssignment(nameof({OutputVariable}));");

            if (IsCapture)
            {
                writer.WriteLine($"{indentString}data.MarkForCapture(nameof({OutputVariable}));");
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

        private bool TryParseConditionalSetting(string line, ParseConditionalCase currentCase)
        {
            foreach (var name in caseSettingNames)
            {
                if (!line.StartsWith($"{name} ", StringComparison.Ordinal) &&
                    !line.StartsWith($"{name}\t", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Descriptor.Parameters.TryGetValue(name, out var parameter) ||
                    !currentCase.Settings.TryGetValue(name, out var setting))
                {
                    return false;
                }

                var temp = line;
                LoliCodeParser.ParseSettingValue(ref temp, setting, parameter);
                return true;
            }

            return false;
        }

        private BlockSetting CloneSetting(BlockSetting source, BlockParameter parameter)
        {
            var clone = parameter.ToBlockSetting();
            CopySettingValues(source, clone);
            return clone;
        }

        private BlockSetting GetCaseSetting(Dictionary<string, BlockSetting> overrides, string name)
        {
            if (overrides != null && overrides.TryGetValue(name, out var setting))
            {
                return setting;
            }

            return Settings[name];
        }

        private static void CopySettingValues(BlockSetting source, BlockSetting destination)
        {
            destination.InputMode = source.InputMode;
            destination.InputVariableName = source.InputVariableName;

            switch (source.FixedSetting)
            {
                case StringSetting src when destination.FixedSetting is StringSetting dest:
                    dest.Value = src.Value;
                    dest.MultiLine = src.MultiLine;
                    break;
                case BoolSetting srcBool when destination.FixedSetting is BoolSetting destBool:
                    destBool.Value = srcBool.Value;
                    break;
                case IntSetting srcInt when destination.FixedSetting is IntSetting destInt:
                    destInt.Value = srcInt.Value;
                    break;
                case FloatSetting srcFloat when destination.FixedSetting is FloatSetting destFloat:
                    destFloat.Value = srcFloat.Value;
                    break;
                case ListOfStringsSetting srcList when destination.FixedSetting is ListOfStringsSetting destList:
                    destList.Value = srcList.Value.ToList();
                    break;
                case DictionaryOfStringsSetting srcDict when destination.FixedSetting is DictionaryOfStringsSetting destDict:
                    destDict.Value = srcDict.Value.ToDictionary(k => k.Key, v => v.Value);
                    break;
            }

            switch (source.InterpolatedSetting)
            {
                case InterpolatedStringSetting src when destination.InterpolatedSetting is InterpolatedStringSetting dest:
                    dest.Value = src.Value;
                    dest.MultiLine = src.MultiLine;
                    break;
                case InterpolatedListOfStringsSetting srcList when destination.InterpolatedSetting is InterpolatedListOfStringsSetting destList:
                    destList.Value = srcList.Value.ToList();
                    break;
                case InterpolatedDictionaryOfStringsSetting srcDict when destination.InterpolatedSetting is InterpolatedDictionaryOfStringsSetting destDict:
                    destDict.Value = srcDict.Value.ToDictionary(k => k.Key, v => v.Value);
                    break;
            }
        }
        public class ParseConditionalCase : ConditionalConstantStringCase
        {
            public ParseMode OverrideMode { get; set; } = ParseMode.LR;
            public Dictionary<string, BlockSetting> Settings { get; } = new Dictionary<string, BlockSetting>();
        }
    }
}
