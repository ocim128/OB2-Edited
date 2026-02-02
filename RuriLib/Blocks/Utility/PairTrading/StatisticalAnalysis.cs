using RuriLib.Helpers;
using System;
using System.Collections.Generic;

namespace RuriLib.Blocks.Utility.PairTrading
{
    /// <summary>
    /// Statistical analysis functions for pair trading calculations.
    /// Delegates to StatisticsHelper for common functions, adds trading-specific calculations.
    /// </summary>
    internal static class StatisticalAnalysis
    {
        private const double PriceEpsilon = 1e-12;

        // Delegate to shared helpers
        public static double Mean(IReadOnlyList<double> values) => StatisticsHelper.Mean(values);
        public static double Std(IReadOnlyList<double> values, double avg) => StatisticsHelper.StandardDeviation(values, avg);
        public static double PearsonCorrelation(IReadOnlyList<double> a, IReadOnlyList<double> b) => StatisticsHelper.PearsonCorrelation(a, b);
        public static double Clamp(double value, double min, double max) => StatisticsHelper.Clamp(value, min, max);
        public static bool IsFinite(double value) => StatisticsHelper.IsFinite(value);
        public static double SumSquares(IReadOnlyList<double> values) => StatisticsHelper.SumOfSquares(values);

        // Trading-specific functions
        public static List<double> CalculateReturns(List<double> closes)
        {
            var returns = new List<double>(Math.Max(0, closes.Count - 1));
            for (var i = 1; i < closes.Count; i++)
            {
                var prev = Math.Max(PriceEpsilon, closes[i - 1]);
                var current = Math.Max(PriceEpsilon, closes[i]);
                returns.Add(Math.Log(current / prev));
            }
            return returns;
        }

        public static List<double> CalculateSpread(List<double> primary, List<double> secondary)
        {
            var length = Math.Min(primary.Count, secondary.Count);
            var spread = new List<double>(length);
            for (var i = 0; i < length; i++)
            {
                var p = Math.Max(PriceEpsilon, primary[i]);
                var s = Math.Max(PriceEpsilon, secondary[i]);
                spread.Add(Math.Log(p) - Math.Log(s));
            }
            return spread;
        }

        public static List<double> CalculateRatio(List<double> primary, List<double> secondary)
        {
            var length = Math.Min(primary.Count, secondary.Count);
            var ratio = new List<double>(length);
            for (var i = 0; i < length; i++)
            {
                var s = secondary[i];
                ratio.Add(s > 0 ? primary[i] / s : 0);
            }
            return ratio;
        }
    }
}
