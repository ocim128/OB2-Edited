using System;
using System.Collections.Generic;
using System.Threading;

namespace RuriLib.Blocks.Utility.PairTrading
{
    /// <summary>
    /// Transfer entropy analysis for determining causal relationships between trading pairs.
    /// </summary>
    internal static class TransferEntropyAnalysis
    {
        private const int MinTransferEntropySamples = 6;
        private const int CancellationCheckInterval = 1000;

        public static TransferEntropyResult CalculateTransferEntropy(
            IReadOnlyList<double> returns1,
            IReadOnlyList<double> returns2,
            int historyLength,
            int bins,
            CancellationToken token)
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

            if (clean1.Count < MinTransferEntropySamples)
            {
                return new TransferEntropyResult(0, 0, 0, "neutral", 0, 0);
            }

            var effectiveHistory = Math.Max(1, historyLength);
            var effectiveBins = Math.Max(2, bins);

            var thresholds1 = QuantileThresholds(clean1, effectiveBins);
            var thresholds2 = QuantileThresholds(clean2, effectiveBins);
            var bins1 = Discretize(clean1, thresholds1);
            var bins2 = Discretize(clean2, thresholds2);

            var te1to2 = ComputeTransferEntropyK(bins1, bins2, effectiveBins, effectiveHistory, token);
            var te2to1 = ComputeTransferEntropyK(bins2, bins1, effectiveBins, effectiveHistory, token);

            var denom = te1to2 + te2to1 + 1e-9;
            var netFlow = StatisticalAnalysis.Clamp((te1to2 - te2to1) / denom, -1, 1);

            var leadingAsset = "neutral";
            if (Math.Abs(netFlow) > 0.1)
            {
                leadingAsset = netFlow > 0 ? "primary" : "secondary";
            }

            var lagInfo = EstimateLag(clean1, clean2, 10, token);
            var significance = StatisticalAnalysis.Clamp((te1to2 + te2to1) / 0.5, 0, 1);

            return new TransferEntropyResult(te1to2, te2to1, netFlow, leadingAsset, lagInfo.Lag, significance);
        }

        public static double ScoreTransferEntropy(TransferEntropyResult entropy)
        {
            var flow = StatisticalAnalysis.Clamp(Math.Abs(entropy.NetFlow), 0, 1);
            var strength = StatisticalAnalysis.Clamp((entropy.Te1To2 + entropy.Te2To1) / 0.2, 0, 1);
            return StatisticalAnalysis.Clamp(flow * 70 + strength * 30, 0, 100);
        }

        private static double[] QuantileThresholds(List<double> values, int bins)
        {
            if (values.Count == 0) return [];
            var sorted = new List<double>(values);
            sorted.Sort();
            var thresholds = new double[Math.Max(0, bins - 1)];
            for (var i = 1; i < bins; i++)
            {
                var idx = (int)Math.Floor((i / (double)bins) * (sorted.Count - 1));
                thresholds[i - 1] = sorted[idx];
            }
            return thresholds;
        }

        private static int[] Discretize(List<double> values, double[] thresholds)
        {
            var result = new int[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];
                var bin = 0;
                while (bin < thresholds.Length && value > thresholds[bin])
                {
                    bin++;
                }
                result[i] = bin;
            }
            return result;
        }

        private static double ComputeTransferEntropyK(int[] xBins, int[] yBins, int bins, int k, CancellationToken token)
        {
            var n = Math.Min(xBins.Length, yBins.Length);
            if (n < k + 2) return 0;

            var maxStates = 50000;
            var effectiveK = Math.Max(1, k);
            while (Math.Pow(bins, effectiveK) > maxStates && effectiveK > 1)
            {
                effectiveK--;
            }

            if (effectiveK == 1)
            {
                return ComputeTransferEntropy(xBins, yBins, bins, token);
            }

            var stateCount = (int)Math.Pow(bins, effectiveK);
            var total = n - effectiveK;
            var count3 = new Dictionary<long, int>();
            var countYX = new Dictionary<long, int>();
            var countYtY = new Dictionary<long, int>();
            var countY = new Dictionary<long, int>();

            for (var t = effectiveK; t < n; t++)
            {
                if (t % CancellationCheckInterval == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                var yState = 0;
                var xState = 0;
                for (var i = 0; i < effectiveK; i++)
                {
                    yState = yState * bins + yBins[t - 1 - i];
                    xState = xState * bins + xBins[t - 1 - i];
                }

                var yt = yBins[t];
                long key3;
                long keyYX;
                long keyYtY;
                try
                {
                    checked
                    {
                        key3 = ((long)yt * stateCount + yState) * stateCount + xState;
                        keyYX = (long)yState * stateCount + xState;
                        keyYtY = (long)yt * stateCount + yState;
                    }
                }
                catch (OverflowException)
                {
                    return 0;
                }

                count3[key3] = (count3.TryGetValue(key3, out var v3) ? v3 : 0) + 1;
                countYX[keyYX] = (countYX.TryGetValue(keyYX, out var vyx) ? vyx : 0) + 1;
                countYtY[keyYtY] = (countYtY.TryGetValue(keyYtY, out var vyt) ? vyt : 0) + 1;
                countY[yState] = (countY.TryGetValue(yState, out var vy) ? vy : 0) + 1;
            }

            var alpha = 1e-6;
            var te = 0.0;
            var index = 0;
            foreach (var kvp in count3)
            {
                if (index % CancellationCheckInterval == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                index++;

                var key3 = kvp.Key;
                var c3 = kvp.Value;

                var xState = (int)(key3 % stateCount);
                var temp = (key3 - xState) / stateCount;
                var yState = (int)(temp % stateCount);
                var yt = (int)((temp - yState) / stateCount);

                var keyYX = (long)yState * stateCount + xState;
                var keyYtY = (long)yt * stateCount + yState;

                var countYXVal = countYX.TryGetValue(keyYX, out var valYX) ? valYX : 0;
                var countYtYVal = countYtY.TryGetValue(keyYtY, out var valYtY) ? valYtY : 0;
                var countYVal = countY.TryGetValue(yState, out var valY) ? valY : 0;

                if (countYXVal == 0 || countYVal == 0) continue;

                var joint = c3 / (double)total;
                var p1 = (c3 + alpha) / (countYXVal + alpha * bins);
                var p2 = (countYtYVal + alpha) / (countYVal + alpha * bins);
                te += joint * Math.Log(p1 / p2, 2);
            }

            return Math.Max(0, te);
        }

        private static double ComputeTransferEntropy(int[] xBins, int[] yBins, int bins, CancellationToken token)
        {
            var n = Math.Min(xBins.Length, yBins.Length);
            if (n < 3) return 0;
            var total = n - 1;

            var count3 = new int[bins * bins * bins];
            var count2 = new int[bins * bins];
            var countYtY1 = new int[bins * bins];
            var countY1 = new int[bins];

            for (var t = 1; t < n; t++)
            {
                if (t % CancellationCheckInterval == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                var yt = yBins[t];
                var y1 = yBins[t - 1];
                var x1 = xBins[t - 1];
                count3[(yt * bins + y1) * bins + x1]++;
                count2[y1 * bins + x1]++;
                countYtY1[yt * bins + y1]++;
                countY1[y1]++;
            }

            var alpha = 1e-6;
            var te = 0.0;
            for (var yt = 0; yt < bins; yt++)
            {
                for (var y1 = 0; y1 < bins; y1++)
                {
                    var baseIdx = yt * bins + y1;
                    var countYY = countYtY1[baseIdx];
                    var countY = countY1[y1];
                    for (var x1 = 0; x1 < bins; x1++)
                    {
                        var idx3 = baseIdx * bins + x1;
                        var c3 = count3[idx3];
                        if (c3 == 0) continue;

                        var joint = c3 / (double)total;
                        var countYX = count2[y1 * bins + x1];
                        var p1 = (c3 + alpha) / (countYX + alpha * bins);
                        var p2 = (countYY + alpha) / (countY + alpha * bins);
                        te += joint * Math.Log(p1 / p2, 2);
                    }
                }
            }

            return Math.Max(0, te);
        }

        private static (int Lag, double Correlation) EstimateLag(List<double> returns1, List<double> returns2, int maxLag, CancellationToken token)
        {
            var n = Math.Min(returns1.Count, returns2.Count);
            if (n < 5) return (0, 0);

            var bestLag = 0;
            var bestCorr = 0.0;

            for (var lag = -maxLag; lag <= maxLag; lag++)
            {
                if ((lag + maxLag) % CancellationCheckInterval == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                if (lag == 0) continue;
                var xs = new List<double>();
                var ys = new List<double>();

                if (lag > 0)
                {
                    for (var i = lag; i < n; i++)
                    {
                        xs.Add(returns1[i - lag]);
                        ys.Add(returns2[i]);
                    }
                }
                else
                {
                    var offset = Math.Abs(lag);
                    for (var i = offset; i < n; i++)
                    {
                        xs.Add(returns1[i]);
                        ys.Add(returns2[i - offset]);
                    }
                }

                if (xs.Count < 5) continue;
                var corr = StatisticalAnalysis.PearsonCorrelation(xs, ys);
                if (Math.Abs(corr) > Math.Abs(bestCorr))
                {
                    bestCorr = corr;
                    bestLag = lag;
                }
            }

            return (Math.Abs(bestLag), bestCorr);
        }
    }
}
