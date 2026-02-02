using System;
using System.Collections.Generic;

namespace RuriLib.Helpers
{
    /// <summary>
    /// Core statistical analysis functions that can be reused across the codebase.
    /// </summary>
    public static class StatisticsHelper
    {
        /// <summary>
        /// Calculates the arithmetic mean of a list of values.
        /// </summary>
        public static double Mean(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            double sum = 0;
            for (var i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }
            return sum / values.Count;
        }

        /// <summary>
        /// Calculates the standard deviation of a list of values.
        /// </summary>
        /// <param name="values">The values.</param>
        /// <param name="mean">The pre-calculated mean (optional - will be calculated if not provided).</param>
        public static double StandardDeviation(IReadOnlyList<double> values, double? mean = null)
        {
            if (values == null || values.Count == 0) return 0;
            var avg = mean ?? Mean(values);
            double sum = 0;
            for (var i = 0; i < values.Count; i++)
            {
                var diff = values[i] - avg;
                sum += diff * diff;
            }
            return Math.Sqrt(sum / values.Count);
        }

        /// <summary>
        /// Calculates the Pearson correlation coefficient between two series.
        /// </summary>
        public static double PearsonCorrelation(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            var n = Math.Min(a.Count, b.Count);
            if (n < 2) return 0;
            var avgA = Mean(a);
            var avgB = Mean(b);
            double num = 0;
            double denA = 0;
            double denB = 0;
            for (var i = 0; i < n; i++)
            {
                var da = a[i] - avgA;
                var db = b[i] - avgB;
                num += da * db;
                denA += da * da;
                denB += db * db;
            }
            if (denA <= 0 || denB <= 0) return 0;
            return num / Math.Sqrt(denA * denB);
        }

        /// <summary>
        /// Calculates the sum of squares of a list of values.
        /// </summary>
        public static double SumOfSquares(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            double sum = 0;
            for (var i = 0; i < values.Count; i++)
            {
                sum += values[i] * values[i];
            }
            return sum;
        }

        /// <summary>
        /// Calculates the minimum value in a list.
        /// </summary>
        public static double Min(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            var min = values[0];
            for (var i = 1; i < values.Count; i++)
            {
                if (values[i] < min) min = values[i];
            }
            return min;
        }

        /// <summary>
        /// Calculates the maximum value in a list.
        /// </summary>
        public static double Max(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            var max = values[0];
            for (var i = 1; i < values.Count; i++)
            {
                if (values[i] > max) max = values[i];
            }
            return max;
        }

        /// <summary>
        /// Calculates the median value of a list.
        /// </summary>
        public static double Median(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            var sorted = new List<double>(values);
            sorted.Sort();
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? (sorted[mid - 1] + sorted[mid]) / 2.0
                : sorted[mid];
        }

        /// <summary>
        /// Clamps a value between a minimum and maximum.
        /// </summary>
        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// Checks if a double value is finite (not NaN or Infinity).
        /// </summary>
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>
        /// Calculates the variance of a list of values.
        /// </summary>
        public static double Variance(IReadOnlyList<double> values, double? mean = null)
        {
            if (values == null || values.Count == 0) return 0;
            var avg = mean ?? Mean(values);
            double sum = 0;
            for (var i = 0; i < values.Count; i++)
            {
                var diff = values[i] - avg;
                sum += diff * diff;
            }
            return sum / values.Count;
        }

        /// <summary>
        /// Normalizes values to a 0-1 range using min-max normalization.
        /// </summary>
        public static List<double> Normalize(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return [];
            var min = Min(values);
            var max = Max(values);
            var range = max - min;
            
            if (range == 0)
            {
                var result = new List<double>(values.Count);
                for (var i = 0; i < values.Count; i++)
                {
                    result.Add(0.5);
                }
                return result;
            }

            var normalized = new List<double>(values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                normalized.Add((values[i] - min) / range);
            }
            return normalized;
        }
    }
}
