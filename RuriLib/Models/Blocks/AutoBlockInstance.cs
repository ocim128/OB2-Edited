using RuriLib.Exceptions;
using RuriLib.Extensions;
using RuriLib.Helpers;
using RuriLib.Helpers.CSharp;
using RuriLib.Helpers.LoliCode;
using RuriLib.Models.Configs;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Variables;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RuriLib.Models.Blocks.Settings.Interpolated;

namespace RuriLib.Models.Blocks;

/// <summary>
/// An instance of a block that was auto generated from exposed methods.
/// </summary>
public partial class AutoBlockInstance : BlockInstance
{
    private string _outputVariable = "output";
    public string OutputVariable
    {
        get => _outputVariable;
        set => _outputVariable = VariableNames.MakeValid(value);
    }

    public bool IsCapture { get; set; }
    public bool Safe { get; set; }

    public AutoBlockInstance(AutoBlockDescriptor descriptor)
        : base(descriptor)
    {
        OutputVariable = descriptor.Id[..1].ToLower(System.Globalization.CultureInfo.CurrentCulture) + descriptor.Id[1..] + "Output";
    }

    public override string ToLC(bool printDefaultParams = false)
    {
        /*
         *   SettingName = "my value"
         *   SettingName = 0
         *   SettingName = @myVariable
         */

        using var writer = new LoliCodeWriter(base.ToLC(printDefaultParams));

        if (Safe)
        {
            _ = writer.AppendLine("SAFE", 2);
        }

        var outVarKind = IsCapture ? "CAP" : "VAR";

        // Write the output variable
        if (Descriptor.ReturnType.HasValue)
        {
            _ = writer.AppendLine($"=> {outVarKind} @{OutputVariable}", 2);
        }

        return writer.ToString();
    }

    public override void FromLC(ref string script, ref int lineNumber)
    {
        // First parse the options that are common to every BlockInstance
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
            }
            else if (line.StartsWith("=>"))
            {
                try
                {
                    var match = MyRegex().Match(line);
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
                catch (Exception ex)
                {
                    throw new LoliCodeParsingException(lineNumber, $"Could not parse the setting: {lineCopy.TruncatePretty(50)} ({ex.Message})");
                }
            }
        }
    }

    public override string ToCSharp(List<string> definedVariables, ConfigSettings settings)
    {
        // If disabled /* code here */

        /*
         * With return type:
         * var myVar = MethodName(data, param1, param2 ...);
         * 
         * Async:
         * await MethodName(data, param1, param2 ...);
         * 
         */

        using var writer = new StringWriter();

        // Detect and declare missing variables from all settings
        var detectedVariables = new HashSet<string>();
        foreach (var setting in Settings.Values)
        {
            // Interpolated strings: detect <var> usage
            if (setting.InterpolatedSetting != null)
            {
                switch (setting.InterpolatedSetting)
                {
                    case InterpolatedStringSetting str:
                        detectedVariables.UnionWith(VariableDetector.DetectFromInterpolatedString(str.Value));
                        break;
                    case InterpolatedListOfStringsSetting list:
                        foreach (var item in list.Value)
                        {
                            detectedVariables.UnionWith(VariableDetector.DetectFromInterpolatedString(item));
                        }

                        break;
                    case InterpolatedDictionaryOfStringsSetting dict:
                        foreach (var kvp in dict.Value)
                        {
                            detectedVariables.UnionWith(VariableDetector.DetectFromInterpolatedString(kvp.Key));
                            detectedVariables.UnionWith(VariableDetector.DetectFromInterpolatedString(kvp.Value));
                        }
                        break;
                }
            }
            // Variable mode: detect direct @var usage
            if (setting.InputMode == SettingInputMode.Variable && !string.IsNullOrEmpty(setting.InputVariableName))
            {
                var baseVar = VariableDetector.ExtractBaseVariableName(setting.InputVariableName);
                if (!string.IsNullOrEmpty(baseVar))
                {
                    _ = detectedVariables.Add(baseVar);
                }
            }
        }
        // Emit NullDynamic declarations for missing variables
        foreach (var missingVar in VariableDetector.GetMissingVariables(detectedVariables, definedVariables))
        {
            writer.WriteLine($"dynamic {missingVar} = RuriLib.Models.NullDynamic.Instance;");
            definedVariables.Add(missingVar);
        }

        // Safe mode, wrap method in try/catch but declare variable outside of it
        if (Safe)
        {
            // If not void, initialize the variable with default value
            // Only do this if we haven't declared the variable yet!
            if (Descriptor.ReturnType.HasValue && !definedVariables.Contains(OutputVariable)
                && !OutputVariable.StartsWith("globals."))
            {
                if (!Disabled)
                {
                    definedVariables.Add(OutputVariable);
                }

                writer.WriteLine($"{GetRuntimeReturnType()} {OutputVariable} = {GetDefaultReturnValue()};");
            }

            writer.WriteLine("try {");

            // Here we already know the variable exists so we just do the assignment
            if (Descriptor.ReturnType.HasValue)
            {
                writer.Write($"{OutputVariable} = ");
            }

            WriteMethod(writer, definedVariables);

            writer.WriteLine("} catch (Exception safeException) {");
            writer.WriteLine("data.ERROR = safeException.PrettyPrint();");
            writer.WriteLine("data.Logger.Log($\"[SAFE MODE] Exception caught and saved to data.ERROR: {data.ERROR}\", LogColors.Tomato); }");
        }
        else
        {
            // If not void, do variable assignment
            if (Descriptor.ReturnType.HasValue)
            {
                if (definedVariables.Contains(OutputVariable) || OutputVariable.StartsWith("globals."))
                {
                    writer.Write($"{OutputVariable} = ");
                }
                else
                {
                    if (!Disabled)
                    {
                        definedVariables.Add(OutputVariable);
                    }

                    writer.Write($"{GetRuntimeReturnType()} {OutputVariable} = ");
                }
            }

            WriteMethod(writer, definedVariables);
        }

        return writer.ToString();
    }

    private void WriteMethod(StringWriter writer, List<string> definedVariables)
    {
        // If async, prepend the await keyword
        if ((Descriptor as AutoBlockDescriptor).Async)
        {
            writer.Write("await ");
        }

        // Append MethodName(data, param1, "param2", param3);
        var autoDescriptor = (AutoBlockDescriptor)Descriptor;
        var parameters = new List<string> { "data" }
            .Concat(Settings.Values.Select(setting =>
            {
                // Get the original parameter type for this setting
                var targetType = autoDescriptor.OriginalParameterTypes.TryGetValue(setting.Name, out var type) ? type : null;
                return CSharpWriter.FromSetting(setting, targetType);
            }));

        writer.Write($"{Descriptor.Id}({string.Join(", ", parameters)})");

        if ((Descriptor as AutoBlockDescriptor).Async)
        {
            writer.WriteLine(".ConfigureAwait(false);");
        }
        else
        {
            writer.WriteLine(";");
        }

        if (Descriptor.ReturnType.HasValue)
        {
            // If the block has a return type, log which variable was written
            writer.WriteLine($"data.LogVariableAssignment(\"{OutputVariable}\");");

            if (IsCapture)
            {
                writer.WriteLine($"data.MarkForCapture(\"{OutputVariable}\");");
            }
        }
        else
        {
            WriteObjectVariableSync(writer, definedVariables);
        }
    }

    private void WriteObjectVariableSync(StringWriter writer, List<string> definedVariables)
    {
        if (!TryGetFixedObjectVariableName(out var variableName))
        {
            return;
        }

        var serializedName = CSharpWriter.SerializeString(variableName);
        var assignment = $"data.Objects.ContainsKey({serializedName}) ? data.Objects[{serializedName}] : RuriLib.Models.NullDynamic.Instance";

        if (definedVariables.Contains(variableName))
        {
            writer.WriteLine($"{variableName} = {assignment};");
        }
        else
        {
            if (!Disabled)
            {
                definedVariables.Add(variableName);
            }

            writer.WriteLine($"dynamic {variableName} = {assignment};");
        }
    }

    private bool TryGetFixedObjectVariableName(out string variableName)
    {
        variableName = string.Empty;

        if (Descriptor.ReturnType.HasValue ||
            !Settings.TryGetValue("variableName", out var setting) ||
            setting.InputMode != SettingInputMode.Fixed ||
            setting.FixedSetting is not StringSetting stringSetting ||
            string.IsNullOrWhiteSpace(stringSetting.Value))
        {
            return false;
        }

        var candidate = stringSetting.Value.Trim();

        if (candidate.Contains('.', StringComparison.Ordinal) || !VariableNames.IsValid(candidate))
        {
            return false;
        }

        variableName = candidate;
        return true;
    }

    /// <summary>
    /// This is needed otherwise when we have blocks made in other plugins they might reference
    /// types from different runtimes and our castings like .AsBool() or .AsInt() will throw a
    /// RuntimeBinderException, so we cannot just write 'var' but we need to explicitly write the type.
    /// </summary>
    /// <exception cref="NotSupportedException"></exception>
    private string GetRuntimeReturnType() => Descriptor.ReturnType switch
    {
        VariableType.Bool => "bool",
        VariableType.ByteArray => "byte[]",
        VariableType.DictionaryOfStrings => "Dictionary<string, string>",
        VariableType.Float => "float",
        VariableType.Int => "int",
        VariableType.ListOfStrings => "List<string>",
        VariableType.String => "string",
        _ => throw new NotSupportedException()
    };

    private string GetDefaultReturnValue() => Descriptor.ReturnType switch
    {
        VariableType.Bool => "false",
        VariableType.ByteArray => "Array.Empty<byte>()",
        VariableType.DictionaryOfStrings => "new()",
        VariableType.Float => "0",
        VariableType.Int => "0",
        VariableType.ListOfStrings => "new()",
        VariableType.String => "string.Empty",
        _ => throw new NotSupportedException()
    };
    [GeneratedRegex("^=> ([A-Za-z]{3}) (.*)$")]
    private static partial Regex MyRegex();
}
