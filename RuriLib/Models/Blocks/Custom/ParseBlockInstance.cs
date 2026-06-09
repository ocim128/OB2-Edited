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
    public partial class ParseBlockInstance : BlockInstance
    {
        private static readonly Regex keyRegex = new("^[A-Z]+KEY ", RegexOptions.Compiled);
        private const string CaseModeToken = "CASEMODE";
        private const string InputSettingName = "input";
        private const string InputOverrideToken = "INPUTOVERRIDE";
        private static readonly string[] caseSettingNames =
        [
            InputSettingName,
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
        ];

        private string outputVariable = "parseOutput";
        public string OutputVariable
        {
            get => outputVariable;
            set => outputVariable = VariableNames.MakeValid(value);
        }

        public bool Recursive { get; set; }
        public bool IsCapture { get; set; }
        public bool Safe { get; set; }
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

            if (Settings.TryGetValue(InputSettingName, out var input))
            {
                TrackInheritedConditionalInput(conditionalCase, input);
            }
        }

        public void SyncInheritedConditionalInputs()
        {
            foreach (var conditionalCase in ConditionalCases)
            {
                SyncInheritedConditionalInput(conditionalCase);
            }
        }

        public void SyncInheritedConditionalInput(ParseConditionalCase conditionalCase)
        {
            NormalizeLegacyConditionalInputOverride(conditionalCase);

            if (IsInputOverridden(conditionalCase) ||
                !Settings.TryGetValue(InputSettingName, out var source) ||
                !Descriptor.Parameters.ContainsKey(InputSettingName) ||
                !conditionalCase.Settings.TryGetValue(InputSettingName, out var destination))
            {
                return;
            }

            if (conditionalCase.InheritedInputSetting != null &&
                !HasSameInputValue(destination, conditionalCase.InheritedInputSetting))
            {
                conditionalCase.InputOverridden = true;
                conditionalCase.InputOverrideExplicitlySet = true;
                return;
            }

            CopySettingValues(source, destination);
            TrackInheritedConditionalInput(conditionalCase, source);
        }

        public ParseBlockInstance(ParseBlockDescriptor descriptor)
            : base(descriptor)
        {
        }

        private BlockSetting CloneSetting(BlockSetting source, BlockParameter parameter)
        {
            var clone = parameter.ToBlockSetting();
            CopySettingValues(source, clone);
            return clone;
        }

        private BlockSetting GetCaseSetting(ParseConditionalCase conditionalCase, string name)
        {
            if (conditionalCase != null &&
                (name != InputSettingName || IsInputOverridden(conditionalCase)) &&
                conditionalCase.Settings.TryGetValue(name, out var setting))
            {
                return setting;
            }

            return Settings[name];
        }

        private bool IsInputOverridden(ParseConditionalCase conditionalCase)
            => conditionalCase?.InputOverridden == true &&
               conditionalCase.Settings.TryGetValue(InputSettingName, out var setting) &&
               (conditionalCase.InputOverrideExplicitlySet || !IsDescriptorDefaultInput(setting));

        private void NormalizeLegacyConditionalInputOverride(ParseConditionalCase conditionalCase)
        {
            if (conditionalCase.InputOverridden &&
                !conditionalCase.InputOverrideExplicitlySet &&
                conditionalCase.Settings.TryGetValue(InputSettingName, out var setting) &&
                IsDescriptorDefaultInput(setting))
            {
                conditionalCase.InputOverridden = false;
            }
        }

        private void TrackInheritedConditionalInput(ParseConditionalCase conditionalCase, BlockSetting source)
        {
            if (Descriptor.Parameters.TryGetValue(InputSettingName, out var parameter))
            {
                conditionalCase.InheritedInputSetting = CloneSetting(source, parameter);
            }
        }

        private static bool HasSameInputValue(BlockSetting first, BlockSetting second)
        {
            if (first.InputMode != second.InputMode)
            {
                return false;
            }

            return first.InputMode switch
            {
                SettingInputMode.Variable => first.InputVariableName == second.InputVariableName,
                SettingInputMode.Fixed when first.FixedSetting is StringSetting firstString &&
                    second.FixedSetting is StringSetting secondString =>
                    firstString.Value == secondString.Value,
                SettingInputMode.Interpolated when first.InterpolatedSetting is InterpolatedStringSetting firstString &&
                    second.InterpolatedSetting is InterpolatedStringSetting secondString =>
                    firstString.Value == secondString.Value,
                _ => false
            };
        }

        private bool IsDescriptorDefaultInput(BlockSetting setting)
        {
            if (!Descriptor.Parameters.TryGetValue(InputSettingName, out var parameter))
            {
                return false;
            }

            if (setting.InputMode != parameter.InputMode)
            {
                return false;
            }

            return parameter switch
            {
                StringParameter stringParameter when setting.InputMode == SettingInputMode.Variable =>
                    setting.InputVariableName == stringParameter.DefaultVariableName,
                StringParameter stringParameter when setting.InputMode == SettingInputMode.Fixed &&
                    setting.FixedSetting is StringSetting fixedSetting =>
                    fixedSetting.Value == stringParameter.DefaultValue,
                StringParameter stringParameter when setting.InputMode == SettingInputMode.Interpolated &&
                    setting.InterpolatedSetting is InterpolatedStringSetting interpolatedSetting =>
                    interpolatedSetting.Value == stringParameter.DefaultValue,
                _ => false
            };
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
            public bool InputOverridden { get; set; }
            public bool InputOverrideExplicitlySet { get; set; }
            internal BlockSetting InheritedInputSetting { get; set; }
            public Dictionary<string, BlockSetting> Settings { get; } = new Dictionary<string, BlockSetting>();
        }

        #region CSharp Code Generation
        public override string ToCSharp(List<string> definedVariables, ConfigSettings settings)
        {
            SyncInheritedConditionalInputs();

            using var writer = new StringWriter();
            var outputType = Recursive ? "List<string>" : "string";
            var defaultReturnValue = Recursive ? "new List<string>()" : "string.Empty";
            var alreadyDefined = definedVariables.Contains(OutputVariable) || OutputVariable.StartsWith("globals.");

            if (!alreadyDefined)
            {
                if (!Disabled)
                {
                    definedVariables.Add(OutputVariable);
                }

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
                    WriteParseMethod(writer, conditionalCase, 4);
                    writer.WriteLine("    data.Logger.LogHeader();");
                    writer.WriteLine($"    data.Logger.Log($\"Conditional parse '{conditionalCase.Name.Replace("\"", "\\\"")}' matched\", LogColors.YellowGreen);");
                    writer.WriteLine("}");
                    first = false;
                }

                writer.WriteLine("if (!__parseCaseMatched)");
                writer.WriteLine("{");
                WriteParseMethod(writer, null, 4);
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

            if (IsCapture)
            {
                writer.WriteLine($"data.MarkForCapture(\"{OutputVariable}\");");
            }

            return writer.ToString();
        }

        private void WriteParseMethod(StringWriter writer, ParseConditionalCase conditionalCase = null, int indent = 0)
        {
            var indentString = new string(' ', indent);
            var modeToUse = conditionalCase?.OverrideMode ?? Mode;
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
            {
                writer.Write("Recursive");
            }

            writer.Write("(data, ");
            writer.Write(CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "input")));

            switch (modeToUse)
            {
                case ParseMode.LR:
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "leftDelim"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "rightDelim"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "caseSensitive"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "prefix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "suffix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "urlEncodeOutput"))}");
                    break;
                case ParseMode.CSS:
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "cssSelector"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "attributeName"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "prefix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "suffix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "urlEncodeOutput"))}");
                    break;
                case ParseMode.XPath:
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "xPath"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "attributeName"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "prefix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "suffix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "urlEncodeOutput"))}");
                    break;
                case ParseMode.Json:
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "jToken"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "prefix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "suffix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "urlEncodeOutput"))}");
                    break;
                case ParseMode.Regex:
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "pattern"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "outputFormat"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "multiLine"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "prefix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "suffix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(conditionalCase, "urlEncodeOutput"))}");
                    break;
            }

            writer.WriteLine(");");
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
        #endregion

        #region LoliCode Serialization and Parsing
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

            SyncInheritedConditionalInputs();
        }

        private void AppendConditionalCases(LoliCodeWriter writer, bool printDefaults)
        {
            foreach (var conditionalCase in ConditionalCases)
            {
                SyncInheritedConditionalInput(conditionalCase);

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
                    if (settingName == InputSettingName && !IsInputOverridden(conditionalCase))
                    {
                        continue;
                    }

                    if (!conditionalCase.Settings.TryGetValue(settingName, out var setting) ||
                        !Descriptor.Parameters.TryGetValue(settingName, out var parameter))
                    {
                        continue;
                    }

                    if (settingName == InputSettingName &&
                        conditionalCase.InputOverrideExplicitlySet &&
                        IsDescriptorDefaultInput(setting))
                    {
                        writer.AppendLine(InputOverrideToken, 4);
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
                else if (currentCase != null && trimmed.StartsWith(InputOverrideToken, StringComparison.OrdinalIgnoreCase))
                {
                    currentCase.InputOverridden = true;
                    currentCase.InputOverrideExplicitlySet = true;
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

                if (name == InputSettingName)
                {
                    currentCase.InputOverridden = true;
                }

                return true;
            }

            return false;
        }
        #endregion
    }
}
