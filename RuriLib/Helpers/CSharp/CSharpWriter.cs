using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Blocks.Settings.Interpolated;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace RuriLib.Helpers.CSharp
{
    /// <summary>
    /// In charge of writing C# snippets that can be executed.
    /// </summary>
    public partial class CSharpWriter
    {
        public readonly struct KeyRenderInfo
        {
            public KeyRenderInfo(string leftExpression, string leftTypeName, string comparison, string rightExpression)
            {
                LeftExpression = leftExpression;
                LeftTypeName = leftTypeName;
                Comparison = comparison;
                RightExpression = rightExpression;
            }

            public string LeftExpression { get; }
            public string LeftTypeName { get; }
            public string Comparison { get; }
            public string RightExpression { get; }
        }

        private static readonly CodeGeneratorOptions codeGenOptions = new()
        {
            BlankLinesBetweenMembers = false
        };

        private static readonly CultureInfo invariantCulture = CultureInfo.InvariantCulture;

#if NET8_0_OR_GREATER
        [GeneratedRegex("<([^>]+)>")]
        private static partial Regex InterpolationRegex();
#else
        private static readonly Regex interpolationRegex = new("<([^>]+)>", RegexOptions.Compiled);
        private static Regex InterpolationRegex() => interpolationRegex;
#endif

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
                case byte by:
                    return by.ToString(invariantCulture);
                case sbyte sb:
                    return sb.ToString(invariantCulture);
                case short sh:
                    return sh.ToString(invariantCulture);
                case ushort ush:
                    return ush.ToString(invariantCulture);
                case int i:
                    return i.ToString(invariantCulture);
                case uint ui:
                    return ui.ToString(invariantCulture);
                case long l:
                    return l.ToString(invariantCulture);
                case ulong ul:
                    return ul.ToString(invariantCulture);
                case float f:
                    return f.ToString("R", invariantCulture) + "F";
                case double d:
                    return d.ToString("R", invariantCulture);
                case decimal dec:
                    return dec.ToString(invariantCulture) + "m";
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
        {
            if (value == null)
                return "null";

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');

            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>
        /// Serializes an interpolated string where &lt;var&gt; is a variable (and sanitizes the '{' and '}' characters).
        /// </summary>
        public static string SerializeInterpString(string value)
        {
            if (value == null)
                return "null";

            var serialized = SerializeString(value);
            var sb = new StringBuilder(serialized)
                .Replace("{", "{{")
                .Replace("}", "}}");

            foreach (Match match in InterpolationRegex().Matches(value))
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

            if (bytes.Length == 0)
                return "Array.Empty<byte>()";

            var sb = new StringBuilder(bytes.Length * 4 + 16);
            sb.Append("new byte[] {");

            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(((int)bytes[i]).ToString(invariantCulture));
            }

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Serializes a list of strings, optionally interpolated.
        /// </summary>
        public static string SerializeList(List<string> list, bool interpolated = false)
        {
            if (list == null)
                return "null";

            if (list.Count == 0)
                return "new List<string>()";

            var sb = new StringBuilder(list.Count * 16 + 32);
            sb.Append("new List<string> {");

            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");

                sb.Append(interpolated ? SerializeInterpString(list[i]) : ToPrimitive(list[i]));
            }

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Serializes a list of strings as a string array, optionally interpolated.
        /// </summary>
        public static string SerializeStringArray(List<string> list, bool interpolated = false)
        {
            if (list == null)
                return "null";

            if (list.Count == 0)
                return "Array.Empty<string>()";

            var sb = new StringBuilder(list.Count * 16 + 24);
            sb.Append("new string[] {");

            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");

                sb.Append(interpolated ? SerializeInterpString(list[i]) : ToPrimitive(list[i]));
            }

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Serializes a dictionary of strings, optionally interpolated.
        /// </summary>
        public static string SerializeDictionary(Dictionary<string, string> dict, bool interpolated = false)
        {
            if (dict == null)
                return "null";

            if (dict.Count == 0)
                return "new Dictionary<string, string>()";

            var sb = new StringBuilder(dict.Count * 24 + 32);
            sb.Append("new Dictionary<string, string> {");

            var first = true;
            foreach (var kvp in dict)
            {
                if (!first)
                    sb.Append(", ");
                first = false;

                var key = interpolated ? SerializeInterpString(kvp.Key) : ToPrimitive(kvp.Key);
                var value = interpolated ? SerializeInterpString(kvp.Value) : ToPrimitive(kvp.Value);
                sb.Append('{').Append(key).Append(", ").Append(value).Append('}');
            }

            sb.Append('}');
            return sb.ToString();
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
        /// Gets render information for a key comparison.
        /// </summary>
        public static KeyRenderInfo GetKeyRenderInfo(Key key)
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
            var leftType = key switch
            {
                BoolKey => "bool",
                StringKey => "string",
                IntKey => "int",
                FloatKey => "float",
                ListKey => "List<string>",
                DictionaryKey => "Dictionary<string, string>",
                _ => throw new Exception("Unknown key type")
            };

            return new KeyRenderInfo(left, leftType, comparison, right);
        }

        /// <summary>
        /// Converts a <paramref name="key"/> to a valid C# snippet.
        /// </summary>
        public static string ConvertKey(Key key)
        {
            var info = GetKeyRenderInfo(key);
            return $"CheckCondition(data, {info.LeftExpression}, {info.Comparison}, {info.RightExpression})";
        }
    }
}
