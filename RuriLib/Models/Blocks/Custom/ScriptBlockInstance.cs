using Newtonsoft.Json;
using RuriLib.Exceptions;
using RuriLib.Extensions;
using RuriLib.Functions.Conversion;
using RuriLib.Functions.Crypto;
using RuriLib.Helpers;
using RuriLib.Helpers.LoliCode;
using RuriLib.Models.Blocks.Custom.Script;
using RuriLib.Models.Configs;
using RuriLib.Models.Variables;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RuriLib.Models.Blocks.Custom;

public partial class ScriptBlockInstance(ScriptBlockDescriptor descriptor) : BlockInstance(descriptor)
{
    public string Script { get; set; } = "var result = x + y;";

    public List<OutputVariable> OutputVariables { get; set; } =
    [
        new OutputVariable
        {
            Name = "result",
            Type = VariableType.Int
        }
    ];

    public string InputVariables { get; set; } = "x,y";
    public Interpreter Interpreter { get; set; } = Interpreter.Jint;

    public override string ToLC(bool printDefaultParams = false)
    {
        /*
         *   INTERPRETER:Jint
         *   INPUT x,y
         *   BEGIN SCRIPT
         *   var result = x + y;
         *   END SCRIPT
         *   OUTPUT Int result
         */

        using var writer = new LoliCodeWriter(base.ToLC(printDefaultParams));
        writer.WriteLine($"INTERPRETER:{Interpreter}");
        writer.WriteLine($"INPUT {InputVariables}");
        writer.WriteLine("BEGIN SCRIPT");
        writer.WriteLine(MyRegex().Replace(Script, ""));
        writer.WriteLine("END SCRIPT");

        foreach (var output in OutputVariables)
        {
            writer.WriteLine($"OUTPUT {output.Type} @{output.Name}");
        }

        return writer.ToString();
    }

    public override void FromLC(ref string script, ref int lineNumber)
    {
        // First parse the options that are common to every BlockInstance
        base.FromLC(ref script, ref lineNumber);

        using var reader = new StringReader(script);
        using var writer = new StringWriter();

        // Parse the interpreter
        var line = reader.ReadLine();
        lineNumber++;

        try
        {
            Interpreter = Enum.Parse<Interpreter>(MyRegex1().Match(line).Groups[1].Value);
        }
        catch
        {
            throw new LoliCodeParsingException(lineNumber, $"Invalid interpreter definition: {line.TruncatePretty(50)}");
        }

        // Parse the input variables
        line = reader.ReadLine();
        lineNumber++;

        try
        {
            InputVariables = MyRegex2().Match(line).Groups[1].Value;
        }
        catch
        {
            throw new LoliCodeParsingException(lineNumber, "Invalid input variables definition");
        }

        _ = reader.ReadLine(); // Read BEGIN SCRIPT
        lineNumber++;

        while ((line = reader.ReadLine()) != null && line != "END SCRIPT")
        {
            lineNumber++;
            writer.WriteLine(line);
        }

        Script = writer.ToString();
        Script = MyRegex().Replace(Script, ""); // Remove blank lines at the end except one

        OutputVariables = [];
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            var match = MyRegex3().Match(line);

            try
            {
                OutputVariables.Add(
                    new OutputVariable
                    {
                        Type = Enum.Parse<VariableType>(match.Groups[1].Value), Name = match.Groups[2].Value
                    });
            }
            catch
            {
                // TODO: Warn the user that the output variable is invalid
            }
        }
    }

    public override string ToCSharp(List<string> definedVariables, ConfigSettings settings)
    {
        using var writer = new StringWriter();
        string scriptHash, scriptPath;
        var resultName = "tmp_" + VariableNames.RandomName(6);
        var engineName = "tmp_" + VariableNames.RandomName(6);
        var scopeName = "tmp_" + VariableNames.RandomName(6);

        // Ensure that all input variables exist; if not, declare them with an empty string default
        if (!string.IsNullOrWhiteSpace(InputVariables))
        {
            foreach (var rawInput in InputVariables.Split(','))
            {
                var input = rawInput.Trim();
                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                if (!definedVariables.Contains(input) && input is not ("input" or "globals" or "data"))
                {
                    writer.WriteLine($"dynamic {input} = RuriLib.Helpers.NullDynamic.Instance;");
                    definedVariables.Add(input);
                }
            }
        }

        switch (Interpreter)
        {
            case Interpreter.Jint:

                scriptHash = HexConverter.ToHexString(Crypto.MD5(Encoding.UTF8.GetBytes(Script)));
                scriptPath = $"Scripts/{scriptHash}.{GetScriptFileExtension(Interpreter)}";

                if (!Directory.Exists("Scripts"))
                {
                    _ = Directory.CreateDirectory("Scripts");
                }

                if (!File.Exists(scriptPath))
                {
                    File.WriteAllText(scriptPath, Script);
                }

                writer.WriteLine($"var {engineName} = new Engine();");

                if (!string.IsNullOrWhiteSpace(InputVariables))
                {
                    foreach (var input in InputVariables.Split(','))
                    {
                        writer.WriteLine($"{engineName}.SetValue(nameof({input}), {input});");
                    }
                }

                writer.WriteLine($"{engineName} = InvokeJint(data, {engineName}, \"{scriptPath}\");");

                foreach (var output in OutputVariables)
                {
                    if (!definedVariables.Contains(output.Name))
                    {
                        writer.Write($"{ToCSharpType(output.Type)} ");
                    }

                    writer.WriteLine($"{output.Name} = {engineName}.GetValue(\"{output.Name}\").{GetJintMethod(output.Type)};");
                }

                break;

            case Interpreter.NodeJS:
                var nodeScript = @$"module.exports = async ({MakeInputs()}) => {{
                        {Script}
                        var noderesult = {{
                        {MakeNodeObject()}
                        }};
                        return noderesult;
                        }}";

                scriptHash = HexConverter.ToHexString(Crypto.MD5(Encoding.UTF8.GetBytes(nodeScript)));

                var escapedScript = JsonConvert.ToString(nodeScript);

                writer.WriteLine($"var {resultName} = await InvokeNode<dynamic>(data, {escapedScript}, new object[] {{ {InputVariables} }}, true, \"{scriptHash}\");");

                foreach (var output in OutputVariables)
                {
                    if (!definedVariables.Contains(output.Name))
                    {
                        writer.Write($"{ToCSharpType(output.Type)} ");
                        definedVariables.Add(output.Name);
                    }

                    writer.WriteLine($"{output.Name} = {GetNodeMethod(resultName, output)};");
                }

                break;

            case Interpreter.IronPython:

                scriptHash = HexConverter.ToHexString(Crypto.MD5(Encoding.UTF8.GetBytes(Script)));
                scriptPath = $"Scripts/{scriptHash}.{GetScriptFileExtension(Interpreter)}";

                if (!Directory.Exists("Scripts"))
                {
                    _ = Directory.CreateDirectory("Scripts");
                }

                if (!File.Exists(scriptPath))
                {
                    File.WriteAllText(scriptPath, Script);
                }

                writer.WriteLine($"var {scopeName} = GetIronPyScope(data);");

                if (!string.IsNullOrWhiteSpace(InputVariables))
                {
                    foreach (var input in InputVariables.Split(','))
                    {
                        writer.WriteLine($"{scopeName}.SetVariable(nameof({input}), {input});");
                    }
                }

                writer.WriteLine($"ExecuteIronPyScript(data, {scopeName}, \"{scriptPath}\");");

                foreach (var output in OutputVariables)
                {
                    if (!definedVariables.Contains(output.Name))
                    {
                        writer.Write($"{ToCSharpType(output.Type)} ");
                    }

                    writer.WriteLine($"{output.Name} = {scopeName}" + output.Type switch
                    {
                        VariableType.ListOfStrings => $".GetVariable<IList<object>>(\"{output.Name}\").Cast<string>().ToList();",
                        VariableType.ByteArray => $".GetVariable<IList<object>>(\"{output.Name}\").Cast<byte>().ToArray();",
                        VariableType.String => throw new NotImplementedException(),
                        VariableType.Int => throw new NotImplementedException(),
                        VariableType.Float => throw new NotImplementedException(),
                        VariableType.Bool => throw new NotImplementedException(),
                        VariableType.DictionaryOfStrings => throw new NotImplementedException(),
                        _ => $".GetVariable<{ToCSharpType(output.Type)}>(\"{output.Name}\");"
                    });
                }

                break;
            default:
                break;
        }

        foreach (var output in OutputVariables)
        {
            writer.WriteLine($"data.LogVariableAssignment(nameof({output.Name}));");
        }

        return writer.ToString();
    }

    private static string GetNodeMethod(string resultName, OutputVariable output) => output.Type switch
    {
        VariableType.Bool => $"{resultName}.GetProperty(\"{output.Name}\").GetBoolean()",
        VariableType.ByteArray => $"{resultName}.GetProperty(\"{output.Name}\").GetBytesFromBase64()",
        VariableType.Float => $"{resultName}.GetProperty(\"{output.Name}\").GetSingle()",
        VariableType.Int => $"{resultName}.GetProperty(\"{output.Name}\").GetInt32()",
        VariableType.String => $"{resultName}.GetProperty(\"{output.Name}\").ToString()",
        VariableType.ListOfStrings => $"((System.Text.Json.JsonElement.ArrayEnumerator){resultName}.GetProperty(\"{output.Name}\").EnumerateArray()).Select(e => e.GetString()).ToList()",
        VariableType.DictionaryOfStrings => $"((System.Text.Json.JsonElement.ObjectEnumerator){resultName}.GetProperty(\"{output.Name}\").EnumerateObject()).ToDictionary(e => e.Name, e => e.Value.GetString())",
        _ => throw new NotImplementedException()
    };

    private static string GetJintMethod(VariableType type) => type switch
    {
        VariableType.Bool => "AsBoolean()",
        VariableType.ByteArray => "TryCast<byte[]>()",
        VariableType.Float => "AsNumber().ToSingle()",
        VariableType.Int => "AsNumber().ToInt()",
        VariableType.ListOfStrings => "AsArray().GetEnumerator().ToEnumerable().Select(j => j.ToString()).ToList()",
        VariableType.String => "ToString()",
        VariableType.DictionaryOfStrings => throw new NotImplementedException(),
        _ => throw new NotImplementedException() // Dictionary not implemented yet
    };

    private static string ToCSharpType(VariableType type) => type switch
    {
        VariableType.Bool => "bool",
        VariableType.ByteArray => "byte[]",
        VariableType.Float => "float",
        VariableType.Int => "int",
        VariableType.ListOfStrings => "List<string>",
        VariableType.String => "string",
        VariableType.DictionaryOfStrings => "Dictionary<string, string>",
        _ => throw new NotImplementedException()
    };

    private static string GetScriptFileExtension(Interpreter interpreter) => interpreter switch
    {
        Interpreter.Jint => "js",
        Interpreter.NodeJS => "js",
        Interpreter.IronPython => "py",
        _ => throw new NotImplementedException()
    };

    private string MakeNodeObject()
        => string.Join("\r\n", OutputVariables.Select(static o => $"  '{o.Name}': {o.Name},"));

    private string MakeInputs()
        => string.Join(",", InputVariables.Split(',').Select(SanitizeInput));

    /// <summary>
    /// Converts input.DATA into DATA
    /// </summary>
    private static string SanitizeInput(string input)
        => MyRegex4().Match(input).Value;
    [GeneratedRegex("(\r\n)*$")]
    private static partial Regex MyRegex();
    [GeneratedRegex("INTERPRETER:([^ ]+)$")]
    private static partial Regex MyRegex1();
    [GeneratedRegex("INPUT (.*)$")]
    private static partial Regex MyRegex2();
    [GeneratedRegex("OUTPUT ([^ ]+) @([^ ]+)$")]
    private static partial Regex MyRegex3();
    [GeneratedRegex("[A-Za-z0-9_]+$")]
    private static partial Regex MyRegex4();
}
