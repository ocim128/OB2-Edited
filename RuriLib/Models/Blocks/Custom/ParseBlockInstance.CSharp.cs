using RuriLib.Helpers.CSharp;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Blocks.Custom.Parse;
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
    }
}
