using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RuriLib.Functions.Parsing
{
    public static class RegexParser
    {

        /// <summary>
        /// Parses a string via a Regex pattern containing Groups, then returns them according to an output format.
        /// </summary>
        /// <param name="input">The string to parse</param>
        /// <param name="pattern">The Regex pattern containing groups</param>
        /// <param name="outputFormat">The output format string, for which [0] will be replaced with the full match,
        /// [1] with the first group etc.</param>
        /// <param name="options">The Regex Options to use</param>
        public static IEnumerable<string> MatchGroupsToString
            (string input, string pattern, string outputFormat, RegexOptions options = RegexOptions.None)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            if (pattern == null)
                throw new ArgumentNullException(nameof(pattern));

            if (outputFormat == null)
                throw new ArgumentNullException(nameof(outputFormat));

            // Fast path for empty input
            if (input.Length == 0)
                yield break;

            // Normalize input for multiline
            var normalizedInput = options.HasFlag(RegexOptions.Multiline) ? 
                input.Replace("\r\n", "\n") : input;

            // Use cached compiled regex for better performance
            var regex = GetOrCreateRegex(pattern, options);
            var matches = regex.Matches(normalizedInput);

            if (matches.Count == 0)
                yield break;

            // Pre-parse output format for better performance
            var formatParts = ParseOutputFormat(outputFormat);
            
            foreach (Match match in matches)
            {
                if (!match.Success) continue;
                
                yield return ApplyFormat(match.Groups, formatParts);
            }
        }

        private static Regex GetOrCreateRegex(string pattern, RegexOptions options)
        {
            return RegexCache.GetOrCreate(pattern, options, compile: true);
        }

        private static (int index, string text)[] ParseOutputFormat(string format)
        {
            var parts = new System.Collections.Generic.List<(int, string)>();
            int lastIndex = 0;
            
            for (int i = 0; i < format.Length - 1; i++)
            {
                if (format[i] == '[' && char.IsDigit(format[i + 1]))
                {
                    int j = i + 1;
                    while (j < format.Length && char.IsDigit(format[j]))
                        j++;
                    
                    if (j < format.Length && format[j] == ']')
                    {
                        if (i > lastIndex)
                            parts.Add((-1, format.Substring(lastIndex, i - lastIndex)));
                        
                        if (int.TryParse(format.Substring(i + 1, j - i - 1), out int index))
                            parts.Add((index, string.Empty));
                        
                        i = j;
                        lastIndex = j + 1;
                    }
                }
            }
            
            if (lastIndex < format.Length)
                parts.Add((-1, format.Substring(lastIndex)));
            
            return parts.ToArray();
        }

        private static string ApplyFormat(GroupCollection groups, (int index, string text)[] formatParts)
        {
            if (formatParts.Length == 0)
                return string.Empty;
            
            // Pre-calculate approximate capacity to reduce allocations
            int capacity = 0;
            foreach (var (index, text) in formatParts)
            {
                capacity += text.Length;
                if (index >= 0 && index < groups.Count)
                    capacity += groups[index].Value.Length;
            }
            
            var sb = new StringBuilder(Math.Max(capacity, 16));
            
            foreach (var (index, text) in formatParts)
            {
                if (text.Length > 0)
                    sb.Append(text);
                
                if (index >= 0 && index < groups.Count)
                    sb.Append(groups[index].Value);
            }
            
            return sb.ToString();
        }
    }
}
