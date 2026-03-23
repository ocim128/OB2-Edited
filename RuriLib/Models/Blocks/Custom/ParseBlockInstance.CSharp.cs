using RuriLib.Helpers.CSharp;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Blocks.Custom.Parse;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Configs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RuriLib.Models.Blocks.Custom
{
    public partial class ParseBlockInstance
    {
        public override string ToCSharp(List<string> definedVariables, ConfigSettings settings)
        {
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
            {
                writer.Write("Recursive");
            }

            writer.Write("(data, ");
            writer.Write(CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "input")));

            switch (modeToUse)
            {
                case ParseMode.LR:
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "leftDelim"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "rightDelim"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "caseSensitive"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "prefix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "suffix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "urlEncodeOutput"))}");
                    break;
                case ParseMode.CSS:
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "cssSelector"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "attributeName"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "prefix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "suffix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "urlEncodeOutput"))}");
                    break;
                case ParseMode.XPath:
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "xPath"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "attributeName"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "prefix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "suffix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "urlEncodeOutput"))}");
                    break;
                case ParseMode.Json:
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "jToken"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "prefix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "suffix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "urlEncodeOutput"))}");
                    break;
                case ParseMode.Regex:
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "pattern"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "outputFormat"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "prefix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "suffix"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "multiLine"))}");
                    writer.Write($", {CSharpWriter.FromSetting(GetCaseSetting(overrideSettings, "urlEncodeOutput"))}");
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
    }
}
