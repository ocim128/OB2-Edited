using System;
using System.Globalization;

namespace RuriLib.Helpers
{
    /// <summary>
    /// Helper methods for parsing values with fallback to different cultures.
    /// </summary>
    public static class ParseHelper
    {
        /// <summary>
        /// Attempts to parse a string as a double, trying InvariantCulture first, then CurrentCulture.
        /// </summary>
        /// <param name="value">The string value to parse.</param>
        /// <param name="result">The parsed result if successful.</param>
        /// <returns>True if parsing succeeded, false otherwise.</returns>
        public static bool TryParseDouble(string? value, out double result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        }

        /// <summary>
        /// Parses a string as a double, returning a default value if parsing fails.
        /// </summary>
        public static double ParseDouble(string? value, double defaultValue = 0)
        {
            return TryParseDouble(value, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Attempts to parse a string as an integer, trying InvariantCulture first, then CurrentCulture.
        /// </summary>
        public static bool TryParseInt(string? value, out int result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                || int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out result);
        }

        /// <summary>
        /// Parses a string as an integer, returning a default value if parsing fails.
        /// </summary>
        public static int ParseInt(string? value, int defaultValue = 0)
        {
            return TryParseInt(value, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Attempts to parse a string as a long integer.
        /// </summary>
        public static bool TryParseLong(string? value, out long result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                || long.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out result);
        }

        /// <summary>
        /// Parses a string as a long, returning a default value if parsing fails.
        /// </summary>
        public static long ParseLong(string? value, long defaultValue = 0)
        {
            return TryParseLong(value, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Attempts to parse a string as a float.
        /// </summary>
        public static bool TryParseFloat(string? value, out float result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                || float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        }

        /// <summary>
        /// Parses a string as a float, returning a default value if parsing fails.
        /// </summary>
        public static float ParseFloat(string? value, float defaultValue = 0)
        {
            return TryParseFloat(value, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Attempts to parse a string as a boolean. Handles "true", "false", "1", "0", "yes", "no".
        /// </summary>
        public static bool TryParseBool(string? value, out bool result)
        {
            result = false;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "true":
                case "1":
                case "yes":
                case "on":
                    result = true;
                    return true;
                case "false":
                case "0":
                case "no":
                case "off":
                    result = false;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Parses a string as a boolean, returning a default value if parsing fails.
        /// </summary>
        public static bool ParseBool(string? value, bool defaultValue = false)
        {
            return TryParseBool(value, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Formats a double value for consistent output.
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <param name="maxDecimals">Maximum number of decimal places.</param>
        public static string FormatDouble(double value, int maxDecimals = 20)
        {
            if (!double.IsFinite(value)) return "0";
            var format = "0." + new string('#', Math.Max(0, maxDecimals));
            var formatted = value.ToString(format, CultureInfo.InvariantCulture);
            return formatted.EndsWith(".", StringComparison.Ordinal) ? formatted.TrimEnd('.') : formatted;
        }

        /// <summary>
        /// Formats a double value with a sign prefix (+ or -).
        /// </summary>
        public static string FormatSigned(double value, int decimals)
        {
            var format = "0." + new string('#', Math.Max(0, decimals));
            var formatted = value.ToString(format, CultureInfo.InvariantCulture);
            if (formatted.EndsWith(".", StringComparison.Ordinal))
            {
                formatted = formatted.TrimEnd('.');
            }
            return (value >= 0 ? "+" : string.Empty) + formatted;
        }

        /// <summary>
        /// Formats a double value as a percentage (0-100 with % suffix).
        /// </summary>
        public static string FormatPercent(double value, int decimals = 0)
        {
            var percent = Math.Max(0, Math.Min(100, value));
            return Math.Round(percent, decimals, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%";
        }
    }
}
