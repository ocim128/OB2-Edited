using System;
using System.Collections.Generic;

namespace RuriLib.Blocks.Utility.PairTrading
{
    /// <summary>
    /// Copula dependence analysis for pair trading.
    /// </summary>
    internal static class CopulaAnalysis
    {
        private const int MinCopulaSamples = 5;
        private const double CopulaTypeThreshold = 0.08;

        public static CopulaResult CalculateCopulaDependence(
            IReadOnlyList<double> returns1,
            IReadOnlyList<double> returns2,
            int windowSize,
            double tailThreshold)
        {
            var clean1 = new List<double>();
            var clean2 = new List<double>();
            var length = Math.Min(returns1.Count, returns2.Count);
            for (var i = 0; i < length; i++)
            {
                var a = returns1[i];
                var b = returns2[i];
                if (!StatisticalAnalysis.IsFinite(a) || !StatisticalAnalysis.IsFinite(b)) continue;
                clean1.Add(a);
                clean2.Add(b);
            }

            if (clean1.Count < MinCopulaSamples)
            {
                return new CopulaResult(0, 0, 0, "gaussian", 0);
            }

            var window = windowSize > 10 ? Math.Min(windowSize, clean1.Count) : clean1.Count;
            var start = clean1.Count - window;
            var slice1 = clean1.GetRange(start, window);
            var slice2 = clean2.GetRange(start, window);

            var ranks1 = RankData(slice1);
            var ranks2 = RankData(slice2);
            var spearman = StatisticalAnalysis.Clamp(StatisticalAnalysis.PearsonCorrelation(ranks1, ranks2), -1, 1);
            var kendallTau = StatisticalAnalysis.Clamp((2.0 / Math.PI) * Math.Asin(spearman), -1, 1);

            var u1 = ToPseudoObservations(ranks1);
            var u2 = ToPseudoObservations(ranks2);
            var threshold = StatisticalAnalysis.Clamp(tailThreshold, 0.5, 0.99);
            var upperThreshold = threshold;
            var lowerThreshold = 1 - threshold;

            var upperCount = 0;
            var lowerCount = 0;
            for (var i = 0; i < u1.Length; i++)
            {
                if (u1[i] > upperThreshold && u2[i] > upperThreshold)
                {
                    upperCount++;
                }
                if (u1[i] < lowerThreshold && u2[i] < lowerThreshold)
                {
                    lowerCount++;
                }
            }

            var count = u1.Length;
            var upper = count > 0
                ? StatisticalAnalysis.Clamp(upperCount / (count * (1 - upperThreshold)), 0, 1)
                : 0;
            var lower = count > 0 && lowerThreshold > 0
                ? StatisticalAnalysis.Clamp(lowerCount / (count * lowerThreshold), 0, 1)
                : 0;

            var copulaType = "gaussian";
            if (upper - lower > CopulaTypeThreshold)
            {
                copulaType = "gumbel";
            }
            else if (lower - upper > CopulaTypeThreshold)
            {
                copulaType = "clayton";
            }

            var tailAsymmetry = Math.Abs(upper - lower);
            var opportunityScore = StatisticalAnalysis.Clamp(
                (1 - Math.Abs(kendallTau)) * 60 + StatisticalAnalysis.Clamp(tailAsymmetry, 0, 1) * 40,
                0,
                100);

            return new CopulaResult(kendallTau, upper, lower, copulaType, opportunityScore);
        }

        private static double[] RankData(IReadOnlyList<double> values)
        {
            var indexed = new List<(double Value, int Index)>(values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                indexed.Add((values[i], i));
            }

            indexed.Sort((a, b) => a.Value.CompareTo(b.Value));

            var ranks = new double[values.Count];
            var iIndex = 0;
            while (iIndex < indexed.Count)
            {
                var j = iIndex + 1;
                while (j < indexed.Count && indexed[j].Value == indexed[iIndex].Value)
                {
                    j++;
                }

                var avgRank = (iIndex + j - 1) / 2.0 + 1;
                for (var k = iIndex; k < j; k++)
                {
                    ranks[indexed[k].Index] = avgRank;
                }

                iIndex = j;
            }

            return ranks;
        }

        private static double[] ToPseudoObservations(double[] ranks)
        {
            var n = ranks.Length;
            if (n == 0) return [];
            var denom = n + 1.0;
            var result = new double[n];
            for (var i = 0; i < n; i++)
            {
                result[i] = ranks[i] / denom;
            }
            return result;
        }
    }
}
