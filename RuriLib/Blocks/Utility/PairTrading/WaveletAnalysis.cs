using System;
using System.Collections.Generic;

namespace RuriLib.Blocks.Utility.PairTrading
{
    /// <summary>
    /// Wavelet decomposition analysis for pair trading spread signals.
    /// </summary>
    internal static class WaveletAnalysis
    {
        public static WaveletResult WaveletDecompose(List<double> spread, WaveletType waveletType, int maxLevels)
        {
            var clean = new List<double>();
            for (var i = 0; i < spread.Count; i++)
            {
                var value = spread[i];
                if (!StatisticalAnalysis.IsFinite(value)) continue;
                clean.Add(value);
            }

            if (clean.Count < 4)
            {
                return new WaveletResult(0, 0, 0);
            }

            var cleanArray = clean.ToArray();
            var originalLength = cleanArray.Length;
            var levels = new List<WaveletLevel>();
            var current = cleanArray;
            var totalEnergy = Math.Max(1e-12, StatisticalAnalysis.SumSquares(current));
            var depth = Math.Max(1, maxLevels);
            var filters = GetWaveletFilters(waveletType);

            for (var level = 0; level < depth; level++)
            {
                if (current.Length < 2) break;

                var approximation = Dwt(current, filters.LoD, filters.HiD, out var detail);
                var detailEnergy = StatisticalAnalysis.SumSquares(detail);

                levels.Add(new WaveletLevel
                {
                    Scale = (int)Math.Pow(2, level + 1),
                    Approximation = approximation,
                    Detail = detail,
                    Energy = detailEnergy / totalEnergy
                });

                current = approximation;
            }

            var reconstruction = levels.Count > 0
                ? levels[levels.Count - 1].Approximation
                : cleanArray;
            for (var i = levels.Count - 1; i >= 0; i--)
            {
                var zeros = new double[reconstruction.Length];
                reconstruction = Idwt(reconstruction, zeros, filters.LoR, filters.HiR);
            }

            var smoothedLength = Math.Min(originalLength, reconstruction.Length);
            var smoothed = new double[smoothedLength];
            if (smoothedLength > 0)
            {
                Array.Copy(reconstruction, smoothed, smoothedLength);
            }

            var dominantCycle = 0;
            var dominantEnergy = 0.0;
            foreach (var level in levels)
            {
                if (level.Energy > dominantEnergy)
                {
                    dominantEnergy = level.Energy;
                    dominantCycle = level.Scale * 2;
                }
            }

            var noiseRatio = 0.0;
            var noiseLevels = Math.Min(2, levels.Count);
            for (var i = 0; i < noiseLevels; i++)
            {
                noiseRatio += levels[i].Energy;
            }
            noiseRatio = StatisticalAnalysis.Clamp(noiseRatio, 0, 1);

            var avg = StatisticalAnalysis.Mean(smoothed);
            var deviation = StatisticalAnalysis.Std(smoothed, avg);
            var spreadZ = deviation > 0 && smoothed.Length > 0
                ? (smoothed[smoothed.Length - 1] - avg) / deviation
                : 0;

            return new WaveletResult(dominantCycle, noiseRatio, spreadZ);
        }

        public static double ScoreWavelet(WaveletResult wavelet)
        {
            var z = Math.Min(3, Math.Abs(wavelet.SpreadZScore));
            var signalClarity = StatisticalAnalysis.Clamp(1 - wavelet.NoiseRatio, 0, 1);
            return StatisticalAnalysis.Clamp((z / 3) * 60 + signalClarity * 40, 0, 100);
        }

        private static WaveletFilters GetWaveletFilters(WaveletType type)
        {
            double[] loD;
            if (type == WaveletType.Haar)
            {
                var sqrt = Math.Sqrt(0.5);
                loD = [sqrt, sqrt];
            }
            else if (type == WaveletType.Coif2)
            {
                loD =
                [
                    -0.0007205494453645122,
                    -0.0018232088707029932,
                    0.0056114348193944995,
                    0.023680171946334084,
                    -0.0594344186464569,
                    -0.0764885990783064,
                    0.41700518442169254,
                    0.8127236354455423,
                    0.3861100668211622,
                    -0.06737255472196302,
                    -0.04146493678175915,
                    0.01638733646359976,
                ];
            }
            else
            {
                loD =
                [
                    0.4829629131445341,
                    0.8365163037378079,
                    0.2241438680420134,
                    -0.1294095225512604
                ];
            }

            var hiD = BuildHighPass(loD);
            var loR = Reverse(loD);
            var hiR = Reverse(hiD);
            return new WaveletFilters(loD, hiD, loR, hiR);
        }

        private static double[] BuildHighPass(double[] low)
        {
            var n = low.Length;
            var high = new double[n];
            for (var i = 0; i < n; i++)
            {
                var sign = i % 2 == 0 ? 1 : -1;
                high[i] = sign * low[n - 1 - i];
            }
            return high;
        }

        private static double[] Reverse(double[] values)
        {
            var reversed = new double[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                reversed[i] = values[values.Length - 1 - i];
            }
            return reversed;
        }

        private static double[] EnsureEven(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return [];
            }

            if (values.Count % 2 == 0)
            {
                var output = new double[values.Count];
                for (var i = 0; i < values.Count; i++)
                {
                    output[i] = values[i];
                }
                return output;
            }

            var extended = new double[values.Count + 1];
            for (var i = 0; i < values.Count; i++)
            {
                extended[i] = values[i];
            }
            extended[extended.Length - 1] = values[values.Count - 1];
            return extended;
        }

        private static double[] Dwt(IReadOnlyList<double> values, double[] loD, double[] hiD, out double[] detail)
        {
            var data = EnsureEven(values);
            var n = data.Length;
            var half = n / 2;
            var approximation = new double[half];
            detail = new double[half];
            var filterLength = loD.Length;

            for (var i = 0; i < half; i++)
            {
                double a = 0;
                double d = 0;
                var baseIndex = 2 * i;
                for (var k = 0; k < filterLength; k++)
                {
                    var idx = (baseIndex + k) % n;
                    var value = data[idx];
                    a += value * loD[k];
                    d += value * hiD[k];
                }
                approximation[i] = a;
                detail[i] = d;
            }

            return approximation;
        }

        private static double[] Idwt(double[] approximation, double[] detail, double[] loR, double[] hiR)
        {
            var n = approximation.Length;
            var outLength = n * 2;
            var output = new double[outLength];
            var filterLength = loR.Length;

            for (var i = 0; i < n; i++)
            {
                var baseIndex = 2 * i;
                var a = approximation[i];
                var d = i < detail.Length ? detail[i] : 0;
                for (var k = 0; k < filterLength; k++)
                {
                    var idx = (baseIndex + k) % outLength;
                    output[idx] += a * loR[k] + d * hiR[k];
                }
            }

            return output;
        }
    }
}
