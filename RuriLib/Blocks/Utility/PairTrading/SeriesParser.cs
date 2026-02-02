using RuriLib.Helpers;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace RuriLib.Blocks.Utility.PairTrading
{
    /// <summary>
    /// Parses and aligns price series data for pair trading analysis.
    /// </summary>
    internal static class SeriesParser
    {
        public static List<double> ParseSeries(BotData data, List<string> values, out int invalidCount)
        {
            invalidCount = 0;
            if (values == null)
            {
                return [];
            }

            if (values.Count == 1)
            {
                var single = values[0] ?? string.Empty;
                var trimmed = single.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    if (TryParseJsonSeries(data, trimmed, out var jsonSeries, out var jsonInvalid))
                    {
                        invalidCount = jsonInvalid;
                        return jsonSeries;
                    }
                }

                if (TryParseDelimitedSeries(trimmed, out var delimitedSeries, out var delimitedInvalid))
                {
                    invalidCount = delimitedInvalid;
                    return delimitedSeries;
                }
            }

            var result = new List<double>(values.Count);
            foreach (var raw in values)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    invalidCount++;
                    result.Add(double.NaN);
                    continue;
                }

                if (ParseHelper.TryParseDouble(raw, out var parsed))
                {
                    result.Add(parsed);
                }
                else
                {
                    invalidCount++;
                    result.Add(double.NaN);
                }
            }

            return result;
        }

        public static (List<double> Primary, List<double> Secondary) AlignSeries(List<double> primary, List<double> secondary, out int droppedCount)
        {
            var length = Math.Min(primary.Count, secondary.Count);
            var alignedPrimary = new List<double>(length);
            var alignedSecondary = new List<double>(length);
            droppedCount = 0;

            for (var i = 0; i < length; i++)
            {
                var p = primary[i];
                var s = secondary[i];
                if (!StatisticsHelper.IsFinite(p) || !StatisticsHelper.IsFinite(s))
                {
                    droppedCount++;
                    continue;
                }
                alignedPrimary.Add(p);
                alignedSecondary.Add(s);
            }

            return (alignedPrimary, alignedSecondary);
        }

        private static bool TryParseJsonSeries(BotData data, string json, out List<double> series, out int invalidCount)
        {
            series = [];
            invalidCount = 0;

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                foreach (var element in root.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Array)
                    {
                        var length = element.GetArrayLength();
                        if (length == 0)
                        {
                            invalidCount++;
                            continue;
                        }

                        var targetIndex = length > 4 ? 4 : length - 1;
                        var valueElement = element[targetIndex];
                        if (TryReadJsonNumber(valueElement, out var value))
                        {
                            series.Add(value);
                        }
                        else
                        {
                            invalidCount++;
                        }
                    }
                    else
                    {
                        if (TryReadJsonNumber(element, out var value))
                        {
                            series.Add(value);
                        }
                        else
                        {
                            invalidCount++;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                return false;
            }
            catch (Exception ex)
            {
                data.Logger.LogError($"Unexpected error parsing JSON series: {ex.Message}", ex);
                return false;
            }

            return series.Count > 0;
        }

        private static bool TryReadJsonNumber(JsonElement element, out double value)
        {
            value = 0;
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.TryGetDouble(out value);
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                var str = element.GetString();
                return ParseHelper.TryParseDouble(str, out value);
            }

            return false;
        }

        private static bool TryParseDelimitedSeries(string input, out List<double> series, out int invalidCount)
        {
            series = [];
            invalidCount = 0;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var separators = new[] { ',', '\n', '\r', '\t', ' ' };
            var tokens = input.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
            {
                return false;
            }

            foreach (var token in tokens)
            {
                if (ParseHelper.TryParseDouble(token, out var parsed))
                {
                    series.Add(parsed);
                }
                else
                {
                    invalidCount++;
                }
            }

            return series.Count > 0;
        }
    }
}
