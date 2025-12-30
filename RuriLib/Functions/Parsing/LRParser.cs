using System;
using System.Collections.Generic;

namespace RuriLib.Functions.Parsing
{
    /// <summary>
    /// Provides parsing methods.
    /// </summary>
    public static class LRParser
    {
        /// <summary>
        /// Parses all strings between <paramref name="leftDelim"/> and <paramref name="rightDelim"/> in the <paramref name="input"/>.
        /// </summary>
        /// <param name="caseSensitive">Whether the case is important</param>
        public static IEnumerable<string> ParseBetween(string input, string leftDelim, string rightDelim, bool caseSensitive = true)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            if (leftDelim == null)
                throw new ArgumentNullException(nameof(leftDelim));

            if (rightDelim == null)
                throw new ArgumentNullException(nameof(rightDelim));

            // No delimiters = return the full input
            if (leftDelim.Length == 0 && rightDelim.Length == 0)
            {
                yield return input;
                yield break;
            }

            var comp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int inputLength = input.Length;
            int leftDelimLength = leftDelim.Length;
            int rightDelimLength = rightDelim.Length;
            int currentIndex = 0;

            while (currentIndex < inputLength)
            {
                int pFrom;
                
                // Find left delimiter
                if (leftDelimLength == 0)
                {
                    pFrom = currentIndex;
                }
                else
                {
                    pFrom = input.IndexOf(leftDelim, currentIndex, comp);
                    if (pFrom == -1)
                        yield break;
                    pFrom += leftDelimLength;
                }

                if (pFrom >= inputLength)
                    yield break;

                // Find right delimiter
                int pTo;
                if (rightDelimLength == 0)
                {
                    pTo = inputLength;
                }
                else
                {
                    pTo = input.IndexOf(rightDelim, pFrom, comp);
                    if (pTo == -1)
                        yield break;
                }

                // Extract substring without creating intermediate strings
                yield return input[pFrom..pTo];

                // Move to next position
                currentIndex = pTo + rightDelimLength;
            }
        }
    }
}
