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

        // Precompiled regexes for performance
        private static readonly Regex InterpVarRegex = new(@"<([^>]+)>", RegexOptions.Compiled);
        private static readonly Regex IdentifierRegex = new(ValidIdentifierRegex, RegexOptions.Compiled);
        private static readonly Regex LoliInterpRegex = new(@"\$""([^""]*)""|'\$([^']*)'", RegexOptions.Compiled);
        private static readonly Regex AtVarRegex = new(@"@([a-zA-Z][a-zA-Z0-9_]*(?:\.[a-zA-Z][a-zA-Z0-9_]*)*)", RegexOptions.Compiled);
        private static readonly Regex ExprIdRegex = new(@"(?:=\s*|[<>=!]+\s*|[\s\(])(" + @"[A-Za-z][A-Za-z0-9_]*" + @")", RegexOptions.Compiled);

        // Cached reserved set
        private static readonly HashSet<string> Reserved = new HashSet<string>(new[]
        {
            // C# keywords
            "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue","decimal",
            "default","delegate","do","double","else","enum","event","explicit","extern","false","finally","fixed","float","for",
            "foreach","goto","if","implicit","in","int","interface","internal","is","lock","long","namespace","new","null","object",
            "operator","out","override","params","private","protected","public","readonly","ref","return","sbyte","sealed","short",
            "sizeof","stackalloc","static","string","struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked",
            "unsafe","ushort","using","virtual","void","volatile","while","yield",
            // Common framework identifiers
            "System","Console","Math","String","DateTime","TimeSpan","Exception","List","Dictionary","Enumerable","Regex","Uri",
            "Convert","Activator",
            // OpenBullet specific reserved words
            "data","globals","input","await","var","dynamic","nameof","Task",
            // LoliCode keywords and operators
            "BOOLKEY","STRINGKEY","INTKEY","FLOATKEY","LISTKEY","DICTKEY","Contains","DoesNotContain","EqualTo","NotEqualTo",
            "GreaterThan","LessThan","GreaterThanOrEqualTo","LessThanOrEqualTo","StartsWith","EndsWith","Exists","DoesNotExist",
            "MatchesRegex","DoesNotMatchRegex","HasLength","DoesNotHaveLength","IsNumeric","IsNotNumeric","IsValidJson",
            "IsNotValidJson","IsValidXml","IsNotValidXml","IsValidUrl","IsNotValidUrl","IsValidEmail","IsNotValidEmail",
            "IF","ELSE","ENDIF","WHILE","ENDWHILE","FOREACH","ENDFOREACH","JUMP","MARK","UNMARK","LOG","CLOG","SET","REPEAT",
            "LOCK","ACQUIRELOCK","RELEASELOCK","TAKEONE","TAKE","END","TRY","CATCH","FINALLY","ENDTRY"
        }, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Detects missing variables from an interpolated string value.
        /// Returns the base variable names (without member access or indexers).
        /// </summary>
        public static HashSet<string> DetectFromInterpolatedString(string interpolatedValue)
        {
            var variables = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(interpolatedValue))
                return variables;

            foreach (Match match in InterpVarRegex.Matches(interpolatedValue))
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
            var variables = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(expression))
                return variables;

            foreach (Match match in IdentifierRegex.Matches(expression))
            {
                var identifier = match.Value;

                if (IsReservedWord(identifier))
                    continue;

                // Skip if it looks like a method call (followed by parentheses)
                var nextIndex = match.Index + match.Length;
                if (nextIndex < expression.Length && expression[nextIndex] == '(')
                    continue;

                // Skip identifiers that are part of a member access (preceded by '.')
                var prevIndex = match.Index - 1;
                if (prevIndex >= 0 && expression[prevIndex] == '.')
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
            var variables = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(statement))
                return variables;

            // Handle interpolated strings in LoliCode (e.g., value = $"<variable>")
            foreach (Match match in LoliInterpRegex.Matches(statement))
            {
                var interpString = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                variables.UnionWith(DetectFromInterpolatedString(interpString));
            }

            // Handle direct variable references (e.g., @variable)
            foreach (Match match in AtVarRegex.Matches(statement))
            {
                var fullVarName = match.Groups[1].Value;
                var baseVariable = ExtractBaseVariableName(fullVarName);

                if (!string.IsNullOrEmpty(baseVariable))
                {
                    variables.Add(baseVariable);
                }
            }

            // Handle variable-like identifiers in expressions (right side of assignments, conditions, etc.)
            foreach (Match match in ExprIdRegex.Matches(statement))
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

            var trimmed = expression.Trim();
            var match = Regex.Match(trimmed, @"^(" + ValidIdentifierRegex + @")", RegexOptions.Compiled);

            if (!match.Success)
                return null;

            var baseVar = match.Groups[1].Value;

            // Don't treat built-in objects as variables that need declaration
            if (baseVar.Equals("data", StringComparison.Ordinal) ||
                baseVar.Equals("globals", StringComparison.Ordinal) ||
                baseVar.Equals("input", StringComparison.Ordinal))
                return null;

            return baseVar;
        }

        /// <summary>
        /// Checks if an identifier is a reserved word that shouldn't be treated as a variable.
        /// </summary>
        private static bool IsReservedWord(string identifier) => Reserved.Contains(identifier);

        /// <summary>
        /// Gets the list of missing variables from all detected variables, excluding those already defined.
        /// </summary>
        public static List<string> GetMissingVariables(HashSet<string> detectedVariables, List<string> definedVariables)
        {
            var defined = new HashSet<string>(definedVariables ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            return detectedVariables
                .Where(v => !defined.Contains(v))
                // Exclude global and input placeholders and root identifiers
                .Where(v => !(v == "globals" || v.StartsWith("globals.", StringComparison.Ordinal)))
                .Where(v => !(v == "input" || v.StartsWith("input.", StringComparison.Ordinal)))
                .Where(v => !(v == "data" || v.StartsWith("data.", StringComparison.Ordinal)))
                .ToList();
        }
    }
}