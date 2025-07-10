using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RuriLib.Helpers.CSharp
{
    /// <summary>
    /// Utility class to detect variables used in code but not yet defined.
    /// </summary>
    public static class VariableDetector
    {
        private static readonly string ValidIdentifierRegex = @"[A-Za-z][A-Za-z0-9_]*";

        /// <summary>
        /// Detects missing variables from an interpolated string value.
        /// Returns the base variable names (without member access or indexers).
        /// </summary>
        public static HashSet<string> DetectFromInterpolatedString(string interpolatedValue)
        {
            var variables = new HashSet<string>();

            if (string.IsNullOrEmpty(interpolatedValue))
                return variables;

            // Find all <variable> patterns in interpolated strings
            var matches = Regex.Matches(interpolatedValue, @"<([^>]+)>");

            foreach (Match match in matches)
            {
                var expression = match.Groups[1].Value.Trim();
                var baseVariable = ExtractBaseVariableName(expression);
                
                if (!string.IsNullOrEmpty(baseVariable))
                {
                    variables.Add(baseVariable);
                }
            }

            return variables;
        }

        /// <summary>
        /// Detects missing variables from a C# expression or statement.
        /// Returns the base variable names (without member access or indexers).
        /// </summary>
        public static HashSet<string> DetectFromExpression(string expression)
        {
            var variables = new HashSet<string>();

            if (string.IsNullOrEmpty(expression))
                return variables;

            // Find variable-like identifiers that are not keywords, literals, or method calls
            var matches = Regex.Matches(expression, ValidIdentifierRegex);

            foreach (Match match in matches)
            {
                var identifier = match.Value;

                // Skip C# keywords, built-in types, and common method names
                if (IsReservedWord(identifier))
                    continue;

                // Skip if it looks like a method call (followed by parentheses)
                var nextIndex = match.Index + match.Length;
                if (nextIndex < expression.Length && expression[nextIndex] == '(')
                    continue;

                variables.Add(identifier);
            }

            return variables;
        }

        /// <summary>
        /// Detects missing variables from a LoliCode statement.
        /// Returns the base variable names (without member access or indexers).
        /// </summary>
        public static HashSet<string> DetectFromLoliCodeStatement(string statement)
        {
            var variables = new HashSet<string>();

            if (string.IsNullOrEmpty(statement))
                return variables;

            // Handle interpolated strings in LoliCode (e.g., value = $"<variable>")
            var interpMatches = Regex.Matches(statement, @"\$""([^""]*)""|'\$([^']*)'");
            foreach (Match match in interpMatches)
            {
                var interpString = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                variables.UnionWith(DetectFromInterpolatedString(interpString));
            }

            // Handle direct variable references (e.g., @variable)
            var varMatches = Regex.Matches(statement, @"@([a-zA-Z][a-zA-Z0-9_]*(?:\.[a-zA-Z][a-zA-Z0-9_]*)*)");
            foreach (Match match in varMatches)
            {
                var fullVarName = match.Groups[1].Value;
                var baseVariable = ExtractBaseVariableName(fullVarName);
                
                if (!string.IsNullOrEmpty(baseVariable))
                {
                    variables.Add(baseVariable);
                }
            }

            // Handle variable-like identifiers in expressions (right side of assignments, conditions, etc.)
            // This is more complex as we need to avoid false positives
            var exprMatches = Regex.Matches(statement, @"(?:=\s*|[<>=!]+\s*|[\s\(])" + @"(" + ValidIdentifierRegex + @")");
            foreach (Match match in exprMatches)
            {
                var identifier = match.Groups[1].Value;
                if (!IsReservedWord(identifier))
                {
                    variables.Add(identifier);
                }
            }

            return variables;
        }

        /// <summary>
        /// Extracts the base variable name from an expression like "var", "var[0]", "var.prop", "var.prop[0]".
        /// </summary>
        public static string ExtractBaseVariableName(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return null;

            // Match the first identifier at the start of the expression
            var match = Regex.Match(expression.Trim(), @"^(" + ValidIdentifierRegex + @")");
            
            if (!match.Success)
                return null;
                
            var baseVar = match.Groups[1].Value;
            
            // Don't treat built-in objects as variables that need declaration
            if (baseVar == "data" || baseVar == "globals" || baseVar == "input")
                return null;
                
            return baseVar;
        }

        /// <summary>
        /// Checks if an identifier is a reserved word that shouldn't be treated as a variable.
        /// </summary>
        private static bool IsReservedWord(string identifier)
        {
            // C# keywords
            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
                "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
                "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
                "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
                "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
                "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
                "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
                "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
                "using", "virtual", "void", "volatile", "while", "yield",
                // Common framework identifiers
                "System", "Console", "Math", "String", "DateTime", "TimeSpan", "Exception", "List",
                "Dictionary", "Enumerable", "Regex", "Uri", "Convert", "Activator",
                // OpenBullet specific reserved words
                "data", "globals", "input", "await", "var", "dynamic", "nameof", "Task"
            };

            return keywords.Contains(identifier);
        }

        /// <summary>
        /// Gets the list of missing variables from all detected variables, excluding those already defined.
        /// </summary>
        public static List<string> GetMissingVariables(HashSet<string> detectedVariables, List<string> definedVariables)
        {
            return detectedVariables
                .Where(v => !definedVariables.Contains(v))
                // Exclude global and input placeholders and root identifiers
                .Where(v => !(v == "globals" || v.StartsWith("globals.")))
                .Where(v => !(v == "input"   || v.StartsWith("input.")))
                .Where(v => !(v == "data"    || v.StartsWith("data.")))
                .ToList();
        }
    }
} 