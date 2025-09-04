using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Blocks.Settings.Interpolated;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RuriLib.Helpers.CSharp
{
    /// <summary>
    /// In charge of writing C# snippets that can be executed.
    /// </summary>
    public class CSharpWriter
    {
        private static readonly CodeGeneratorOptions codeGenOptions = new()
        {
            BlankLinesBetweenMembers = false
        };

        /// <summary>
        /// Converts a <paramref name="setting"/> to a valid C# snippet.
        /// </summary>
        public static string FromSetting(BlockSetting setting)
        {
            return FromSetting(setting, null);
        }

        /// <summary>
        /// Converts a <paramref name="setting"/> to a valid C# snippet with optional target type conversion.
        /// </summary>
        /// <param name="setting">The block setting to convert</param>
        /// <param name="targetType">The target parameter type for conversion (e.g., string[] vs List&lt;string&gt;)</param>
        public static string FromSetting(BlockSetting setting, Type targetType)
        {
            if (setting.InputMode == SettingInputMode.Variable)
            {
                // Check if this is a built-in property access (data.*, globals.*, input.*)
                if (setting.InputVariableName.StartsWith("data.") ||
                    setting.InputVariableName.StartsWith("globals.") ||
                    setting.InputVariableName.StartsWith("input."))
                {
                    // For input.* properties, return direct access since they're already the correct type from ExpandoObject
                    if (setting.InputVariableName.StartsWith("input."))
                    {
                        return $"({GetTypeName(setting)})({setting.InputVariableName})";
                    }
                    else
                    {
                        // For data.* and globals.* properties, apply casting
                        var expr = $"({GetTypeName(setting)})({setting.InputVariableName}){GetCasting(setting, false)}";
                        return expr;
                    }
                }
                else
                {
                    // Always cast the variable to object and use DynamicAs* extensions so that
                    // even NullDynamic (or any other dynamic object) is handled safely.
                    var expr = $"({GetTypeName(setting)})((object){setting.InputVariableName}){GetCasting(setting, true)}";
                    return expr;
                }
            }

            if (setting.InputMode == SettingInputMode.Interpolated)
            {
                return setting.InterpolatedSetting switch
                {
                    InterpolatedStringSetting x => SerializeInterpString(x.Value),
                    InterpolatedListOfStringsSetting x => targetType == typeof(string[]) ? SerializeStringArray(x.Value, true) : SerializeList(x.Value, true),
                    InterpolatedDictionaryOfStringsSetting x => SerializeDictionary(x.Value, true),
                    _ => throw new NotImplementedException()
                };

            }

            return setting.FixedSetting switch
            {
                BoolSetting x => ToPrimitive(x.Value),
                ByteArraySetting x => SerializeByteArray(x.Value),
                DictionaryOfStringsSetting x => SerializeDictionary(x.Value),
                FloatSetting x => ToPrimitive(x.Value),
                IntSetting x => ToPrimitive(x.Value),
                ListOfStringsSetting x => targetType == typeof(string[]) ? SerializeStringArray(x.Value) : SerializeList(x.Value),
                StringSetting x => ToPrimitive(x.Value),
                EnumSetting x => $"{x.EnumType.FullName}.{x.Value}",
                _ => throw new NotImplementedException()
            };
        }

        /// <summary>
        /// Converts a <paramref name="value"/> to a C# primitive.
        /// Adds fast paths for common primitives to avoid CodeDom overhead.
        /// </summary>
        public static string ToPrimitive(object value)
        {
            if (value is null) return "null";
            switch (value)
            {
                case string s:
                    return SerializeString(s);
                case bool b:
                    return b ? "true" : "false";
                case int i:
                    return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case float f:
                    return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "F";
                case double d:
                    return d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                case byte by:
                    return by.ToString(System.Globalization.CultureInfo.InvariantCulture);
                default:
                    {
                        using var writer = new StringWriter();
                        using var provider = CodeDomProvider.CreateProvider("CSharp");
                        provider.GenerateCodeFromExpression(new CodePrimitiveExpression(value), writer, codeGenOptions);
                        return writer.ToString();
                    }
            }
        }

        /// <summary>
        /// Serializes a literal without splitting it on multiple lines like <see cref="ToPrimitive(object)"/> does..
        /// </summary>
        public static string SerializeString(string value)
            => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

        /// <summary>
        /// Serializes an interpolated string where &lt;var&gt; is a variable (and sanitizes the '{' and '}' characters).
        /// </summary>
        public static string SerializeInterpString(string value)
        {
            var sb = new StringBuilder(SerializeString(value))
                .Replace("{", "{{")
                .Replace("}", "}}");

            foreach (Match match in Regex.Matches(value, @"<([^>]+)>"))
            {
                var variable = match.Groups[1].Value;
                sb.Replace(match.Groups[0].Value.Replace("\\", "\\\\").Replace("\"", "\\\""), '{' + variable + '}');
            }

            var result = '$' + sb.ToString();
            return result;
        }

        /// <summary>
        /// Serializes a byte array.
        /// </summary>
        public static string SerializeByteArray(byte[] bytes)
        {
            if (bytes == null)
                return "null";

            using var writer = new StringWriter();
            writer.Write("new byte[] {");
            writer.Write(string.Join(", ", bytes.Select(b => Convert.ToInt32(b).ToString())));
            writer.Write("}");
            return writer.ToString();
        }

        /// <summary>
        /// Serializes a list of strings, optionally interpolated.
        /// </summary>
        public static string SerializeList(List<string> list, bool interpolated = false)
        {
            if (list == null)
                return "null";

            using var writer = new StringWriter();
            writer.Write("new List<string> {");

            var toWrite = list.Select(e => interpolated
                ? SerializeInterpString(e)
                : ToPrimitive(e));

            writer.Write(string.Join(", ", toWrite));
            writer.Write("}");
            return writer.ToString();
        }

        /// <summary>
        /// Serializes a list of strings as a string array, optionally interpolated.
        /// </summary>
        public static string SerializeStringArray(List<string> list, bool interpolated = false)
        {
            if (list == null)
                return "null";

            using var writer = new StringWriter();
            writer.Write("new string[] {");

            var toWrite = list.Select(e => interpolated
                ? SerializeInterpString(e)
                : ToPrimitive(e));

            writer.Write(string.Join(", ", toWrite));
            writer.Write("}");
            return writer.ToString();
        }

        /// <summary>
        /// Serializes a dictionary of strings, optionally interpolated.
        /// </summary>
        public static string SerializeDictionary(Dictionary<string, string> dict, bool interpolated = false)
        {
            if (dict == null)
                return "null";

            using var writer = new StringWriter();
            writer.Write("new Dictionary<string, string> {");

            var toWrite = dict.Select(kvp => interpolated
                ? $"{{{SerializeInterpString(kvp.Key)}, {SerializeInterpString(kvp.Value)}}}"
                : $"{{{ToPrimitive(kvp.Key)}, {ToPrimitive(kvp.Value)}}}");

            writer.Write(string.Join(", ", toWrite));
            writer.Write("}");
            return writer.ToString();
        }

        private static string GetCasting(BlockSetting setting, bool dynamic = false)
        {
            if (setting.FixedSetting == null)
                throw new ArgumentNullException(nameof(setting));

            var method = setting.FixedSetting switch
            {
                BoolSetting _ => "AsBool()",
                ByteArraySetting _ => "AsBytes()",
                DictionaryOfStringsSetting _ => "AsDict()",
                FloatSetting _ => "AsFloat()",
                IntSetting _ => "AsInt()",
                ListOfStringsSetting _ => "AsList()",
                StringSetting _ => "AsString()",
                _ => throw new NotImplementedException()
            };

            // E.g. .DynamicAsString() for dynamics, .AsString() for normal types
            return dynamic ? $".Dynamic{method}" : $".{method}";
        }

        private static string GetTypeName(BlockSetting setting)
        {
            if (setting.FixedSetting == null)
                throw new ArgumentNullException(nameof(setting));

            return setting.FixedSetting switch
            {
                BoolSetting _ => "bool",
                ByteArraySetting _ => "byte[]",
                DictionaryOfStringsSetting _ => "Dictionary<string, string>",
                FloatSetting _ => "float",
                IntSetting _ => "int",
                ListOfStringsSetting _ => "List<string>",
                StringSetting _ => "string",
                _ => throw new NotImplementedException()
            };
        }

        /// <summary>
        /// Converts a <paramref name="key"/> to a valid C# snippet.
        /// </summary>
        public static string ConvertKey(Key key)
        {
            var comparison = key switch
            {
                BoolKey x => $"BoolComparison.{x.Comparison}",
                StringKey x => $"StrComparison.{x.Comparison}",
                IntKey x => $"NumComparison.{x.Comparison}",
                FloatKey x => $"NumComparison.{x.Comparison}",
                ListKey x => $"ListComparison.{x.Comparison}",
                DictionaryKey x => $"DictComparison.{x.Comparison}",
                _ => throw new Exception("Unknown key type")
            };

            var left = FromSetting(key.Left);
            var right = FromSetting(key.Right);

            return $"CheckCondition(data, {left}, {comparison}, {right})";
        }
    }
}
