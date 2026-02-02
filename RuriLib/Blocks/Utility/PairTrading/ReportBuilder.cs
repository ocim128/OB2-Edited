using RuriLib.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RuriLib.Blocks.Utility.PairTrading
{
    /// <summary>
    /// Builds reports and formats output for pair trading analysis.
    /// </summary>
    internal static class ReportBuilder
    {
        public static Dictionary<string, string> BuildEmptyResult(int primaryCount, int secondaryCount, int primaryInvalid, int secondaryInvalid, int alignedCount, int droppedCount)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["primary.count"] = primaryCount.ToString(CultureInfo.InvariantCulture),
                ["secondary.count"] = secondaryCount.ToString(CultureInfo.InvariantCulture),
                ["aligned.count"] = alignedCount.ToString(CultureInfo.InvariantCulture),
                ["correlation"] = "0",
                ["spread.mean"] = "0",
                ["spread.std"] = "0",
                ["spread.zscore"] = "0",
                ["ratio"] = "0",
                ["opportunity.score"] = "0",
                ["opportunity.spreadScore"] = "0",
                ["opportunity.methodAverage"] = "0"
            };

            if (primaryInvalid > 0)
            {
                result["primary.invalid"] = primaryInvalid.ToString(CultureInfo.InvariantCulture);
            }

            if (secondaryInvalid > 0)
            {
                result["secondary.invalid"] = secondaryInvalid.ToString(CultureInfo.InvariantCulture);
            }

            if (droppedCount > 0)
            {
                result["aligned.dropped"] = droppedCount.ToString(CultureInfo.InvariantCulture);
            }

            return result;
        }

        public static Dictionary<string, string> BuildResult(
            int primaryCount, int secondaryCount, int primaryInvalid, int secondaryInvalid,
            int alignedCount, int droppedCount, double correlation, double spreadMean, double spreadStd,
            double spreadZ, double ratio, double overallOpportunity, double spreadOpportunity, double methodAverage,
            CopulaResult? copula, WaveletResult? wavelet, TransferEntropyResult? entropy,
            CorrelationVelocityResult? correlationVelocity = null, VolatilityAdjustedSpreadResult? volatilitySpread = null)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["primary.count"] = primaryCount.ToString(CultureInfo.InvariantCulture),
                ["secondary.count"] = secondaryCount.ToString(CultureInfo.InvariantCulture),
                ["aligned.count"] = alignedCount.ToString(CultureInfo.InvariantCulture),
                ["correlation"] = FormatDouble(correlation),
                ["spread.mean"] = FormatDouble(spreadMean),
                ["spread.std"] = FormatDouble(spreadStd),
                ["spread.zscore"] = FormatDouble(spreadZ),
                ["ratio"] = FormatDouble(ratio),
                ["opportunity.score"] = FormatDouble(overallOpportunity),
                ["opportunity.spreadScore"] = FormatDouble(spreadOpportunity),
                ["opportunity.methodAverage"] = FormatDouble(methodAverage)
            };

            if (primaryInvalid > 0)
            {
                result["primary.invalid"] = primaryInvalid.ToString(CultureInfo.InvariantCulture);
            }

            if (secondaryInvalid > 0)
            {
                result["secondary.invalid"] = secondaryInvalid.ToString(CultureInfo.InvariantCulture);
            }

            if (droppedCount > 0)
            {
                result["aligned.dropped"] = droppedCount.ToString(CultureInfo.InvariantCulture);
            }

            if (copula.HasValue)
            {
                var c = copula.Value;
                result["copula.kendallTau"] = FormatDouble(c.KendallTau);
                result["copula.tailUpper"] = FormatDouble(c.TailUpper);
                result["copula.tailLower"] = FormatDouble(c.TailLower);
                result["copula.type"] = c.CopulaType;
                result["copula.opportunityScore"] = FormatDouble(c.OpportunityScore);
            }

            if (wavelet.HasValue)
            {
                var w = wavelet.Value;
                result["wavelet.dominantCycle"] = FormatDouble(w.DominantCycle);
                result["wavelet.noiseRatio"] = FormatDouble(w.NoiseRatio);
                result["wavelet.spreadZScore"] = FormatDouble(w.SpreadZScore);
                result["wavelet.signalScore"] = FormatDouble(WaveletAnalysis.ScoreWavelet(w));
            }

            if (entropy.HasValue)
            {
                var e = entropy.Value;
                result["entropy.te1to2"] = FormatDouble(e.Te1To2);
                result["entropy.te2to1"] = FormatDouble(e.Te2To1);
                result["entropy.netFlow"] = FormatDouble(e.NetFlow);
                result["entropy.leadingAsset"] = e.LeadingAsset;
                result["entropy.lagBars"] = FormatDouble(e.LagBars);
                result["entropy.significance"] = FormatDouble(e.Significance);
                result["entropy.score"] = FormatDouble(TransferEntropyAnalysis.ScoreTransferEntropy(e));
            }

            if (correlationVelocity.HasValue)
            {
                var cv = correlationVelocity.Value;
                result["correlationVelocity.current"] = FormatDouble(cv.CurrentCorrelation);
                result["correlationVelocity.previous"] = FormatDouble(cv.PreviousCorrelation);
                result["correlationVelocity.velocity"] = FormatDouble(cv.Velocity);
                result["correlationVelocity.acceleration"] = FormatDouble(cv.Acceleration);
                result["correlationVelocity.regime"] = cv.Regime;
            }

            if (volatilitySpread.HasValue)
            {
                var vs = volatilitySpread.Value;
                result["volatilitySpread.rawZScore"] = FormatDouble(vs.RawZScore);
                result["volatilitySpread.adjustedZScore"] = FormatDouble(vs.AdjustedZScore);
                result["volatilitySpread.combinedVolatility"] = FormatDouble(vs.CombinedVolatility);
                result["volatilitySpread.primaryVolatility"] = FormatDouble(vs.PrimaryVolatility);
                result["volatilitySpread.secondaryVolatility"] = FormatDouble(vs.SecondaryVolatility);
                result["volatilitySpread.signalStrength"] = FormatDouble(vs.SignalStrength);
                result["volatilitySpread.signalQuality"] = vs.SignalQuality;
            }

            var notes = BuildNotes(spreadZ, correlation, entropy, correlationVelocity, volatilitySpread);
            if (notes.Count > 0)
            {
                result["notes.count"] = notes.Count.ToString(CultureInfo.InvariantCulture);
                result["notes"] = string.Join("\n", notes);
                for (var i = 0; i < notes.Count; i++)
                {
                    result[$"notes.{i}"] = notes[i];
                }
            }

            result["report"] = BuildReport(result, notes);

            return result;
        }

        private static List<string> BuildNotes(double spreadZ, double correlation, TransferEntropyResult? entropy,
            CorrelationVelocityResult? correlationVelocity = null, VolatilityAdjustedSpreadResult? volatilitySpread = null)
        {
            var notes = new List<string>();
            var z = spreadZ;
            if (Math.Abs(z) >= 2)
            {
                notes.Add($"Spread Z-score {ParseHelper.FormatSigned(z, 2)} sigma: consider mean-reversion entry.");
            }
            else if (Math.Abs(z) >= 1)
            {
                notes.Add($"Spread Z-score {ParseHelper.FormatSigned(z, 2)} sigma: divergence building.");
            }
            else
            {
                notes.Add("Spread is near its mean; low divergence right now.");
            }

            if (Math.Abs(correlation) >= 0.7)
            {
                notes.Add($"Returns correlation is strong ({ParseHelper.FormatSigned(correlation, 2)}).");
            }
            else if (Math.Abs(correlation) >= 0.4)
            {
                notes.Add($"Returns correlation is moderate ({ParseHelper.FormatSigned(correlation, 2)}).");
            }
            else
            {
                notes.Add($"Returns correlation is weak ({ParseHelper.FormatSigned(correlation, 2)}).");
            }

            if (entropy.HasValue && Math.Abs(entropy.Value.NetFlow) > 0.1)
            {
                var leader = entropy.Value.LeadingAsset == "primary" ? "Primary" : "Secondary";
                notes.Add($"{leader} leads by ~{entropy.Value.LagBars} bars: monitor for follow-through.");
            }

            // Correlation Velocity notes
            if (correlationVelocity.HasValue)
            {
                var cv = correlationVelocity.Value;
                switch (cv.Regime)
                {
                    case "breaking_down":
                        notes.Add($"⚠️ REGIME CHANGE: Correlation breaking down ({ParseHelper.FormatSigned(cv.Velocity, 4)}/bar). Avoid new positions.");
                        break;
                    case "weakening":
                        notes.Add($"⚡ Correlation weakening ({ParseHelper.FormatSigned(cv.Velocity, 4)}/bar). Monitor for regime change.");
                        break;
                    case "recovering":
                        notes.Add($"📈 Correlation recovering ({ParseHelper.FormatSigned(cv.Velocity, 4)}/bar). Potential opportunity emerging.");
                        break;
                    case "strengthening":
                        notes.Add($"🔥 Correlation strengthening ({ParseHelper.FormatSigned(cv.Velocity, 4)}/bar). Favorable conditions.");
                        break;
                    case "stable_strong":
                        notes.Add("✅ Correlation stable and strong. Good for pair trading.");
                        break;
                    case "stable_weak":
                        notes.Add("⚠️ Correlation stable but weak. Not ideal for pair trading.");
                        break;
                }

                // Add acceleration note if significant
                if (Math.Abs(cv.Acceleration) > 0.001)
                {
                    var accelDir = cv.Acceleration > 0 ? "accelerating" : "decelerating";
                    notes.Add($"Correlation velocity is {accelDir} ({ParseHelper.FormatSigned(cv.Acceleration, 5)}).");
                }
            }

            // Volatility-Adjusted Spread notes
            if (volatilitySpread.HasValue)
            {
                var vs = volatilitySpread.Value;
                switch (vs.SignalQuality)
                {
                    case "premium":
                        notes.Add($"💎 PREMIUM SIGNAL: High spread ({ParseHelper.FormatSigned(vs.AdjustedZScore, 2)}) with low volatility. Best opportunity.");
                        break;
                    case "strong":
                        notes.Add($"💪 Strong signal quality (adj. Z: {ParseHelper.FormatSigned(vs.AdjustedZScore, 2)}). Good opportunity.");
                        break;
                    case "moderate":
                        notes.Add($"📊 Moderate signal quality (adj. Z: {ParseHelper.FormatSigned(vs.AdjustedZScore, 2)}). Proceed with caution.");
                        break;
                    case "noisy":
                        notes.Add($"🔊 High volatility ({vs.CombinedVolatility:P1}) makes signal noisy. Wait for calmer conditions.");
                        break;
                    case "weak":
                        notes.Add("📉 Weak signal. No clear opportunity at this time.");
                        break;
                }

                // Volatility imbalance note
                if (vs.PrimaryVolatility > 0 && vs.SecondaryVolatility > 0)
                {
                    var volRatio = vs.PrimaryVolatility / vs.SecondaryVolatility;
                    if (volRatio > 2.0 || volRatio < 0.5)
                    {
                        var higher = volRatio > 1 ? "Primary" : "Secondary";
                        notes.Add($"⚖️ Volatility imbalance: {higher} is {Math.Max(volRatio, 1/volRatio):F1}x more volatile.");
                    }
                }
            }

            return notes;
        }

        private static string BuildReport(Dictionary<string, string> values, List<string> notes)
        {
            var lines = new List<string>
            {
                "Overview",
                $"Opportunity {FormatPercentValue(GetDouble(values, "opportunity.score"), 0)}",
                $"Correlation {ParseHelper.FormatSigned(GetDouble(values, "correlation"), 3)}",
                $"Spread Z {ParseHelper.FormatSigned(GetDouble(values, "spread.zscore"), 2)}",
                $"Ratio {FormatRounded(GetDouble(values, "ratio"), 6)}",
                $"Aligned Bars {FormatRounded(GetDouble(values, "aligned.count"), 0)}"
            };

            if (values.ContainsKey("copula.kendallTau"))
            {
                lines.Add(string.Empty);
                lines.Add("Copula Dependence");
                lines.Add($"Kendall Tau {FormatRounded(GetDouble(values, "copula.kendallTau"), 3)}");
                lines.Add($"Upper Tail {FormatRounded(GetDouble(values, "copula.tailUpper"), 2)}");
                lines.Add($"Lower Tail {FormatRounded(GetDouble(values, "copula.tailLower"), 2)}");
                lines.Add($"Copula Type {GetValue(values, "copula.type")}");
                lines.Add($"Opportunity {FormatPercentValue(GetDouble(values, "copula.opportunityScore"), 0)}");
            }

            if (values.ContainsKey("wavelet.dominantCycle"))
            {
                lines.Add(string.Empty);
                lines.Add("Wavelet Signals");
                lines.Add($"Dominant Cycle {FormatRounded(GetDouble(values, "wavelet.dominantCycle"), 0)} bars");
                lines.Add($"Noise Ratio {ParseHelper.FormatPercent(GetDouble(values, "wavelet.noiseRatio") * 100)}");
                lines.Add($"Spread Z {ParseHelper.FormatSigned(GetDouble(values, "wavelet.spreadZScore"), 2)}");
                lines.Add($"Signal Score {FormatPercentValue(GetDouble(values, "wavelet.signalScore"), 0)}");
            }

            if (values.ContainsKey("entropy.te1to2"))
            {
                lines.Add(string.Empty);
                lines.Add("Transfer Entropy");
                lines.Add($"TE 1 -> 2 {FormatRounded(GetDouble(values, "entropy.te1to2"), 4)}");
                lines.Add($"TE 2 -> 1 {FormatRounded(GetDouble(values, "entropy.te2to1"), 4)}");
                lines.Add($"Net Flow {ParseHelper.FormatSigned(GetDouble(values, "entropy.netFlow"), 2)}");
                lines.Add($"Leader {GetValue(values, "entropy.leadingAsset")}");
                lines.Add($"Lag {FormatRounded(GetDouble(values, "entropy.lagBars"), 0)} bars");
                lines.Add($"Confidence {ParseHelper.FormatPercent(GetDouble(values, "entropy.significance") * 100)}");
            }

            if (values.ContainsKey("correlationVelocity.velocity"))
            {
                lines.Add(string.Empty);
                lines.Add("Correlation Velocity");
                lines.Add($"Current {ParseHelper.FormatSigned(GetDouble(values, "correlationVelocity.current"), 3)}");
                lines.Add($"Previous {ParseHelper.FormatSigned(GetDouble(values, "correlationVelocity.previous"), 3)}");
                lines.Add($"Velocity {ParseHelper.FormatSigned(GetDouble(values, "correlationVelocity.velocity"), 5)}/bar");
                lines.Add($"Acceleration {ParseHelper.FormatSigned(GetDouble(values, "correlationVelocity.acceleration"), 6)}");
                lines.Add($"Regime {GetValue(values, "correlationVelocity.regime")}");
            }

            if (values.ContainsKey("volatilitySpread.adjustedZScore"))
            {
                lines.Add(string.Empty);
                lines.Add("Volatility-Adjusted Spread");
                lines.Add($"Raw Z {ParseHelper.FormatSigned(GetDouble(values, "volatilitySpread.rawZScore"), 2)}");
                lines.Add($"Adjusted Z {ParseHelper.FormatSigned(GetDouble(values, "volatilitySpread.adjustedZScore"), 2)}");
                lines.Add($"Combined Vol {ParseHelper.FormatPercent(GetDouble(values, "volatilitySpread.combinedVolatility") * 100)}");
                lines.Add($"Primary Vol {ParseHelper.FormatPercent(GetDouble(values, "volatilitySpread.primaryVolatility") * 100)}");
                lines.Add($"Secondary Vol {ParseHelper.FormatPercent(GetDouble(values, "volatilitySpread.secondaryVolatility") * 100)}");
                lines.Add($"Signal Strength {FormatPercentValue(GetDouble(values, "volatilitySpread.signalStrength"), 0)}");
                lines.Add($"Signal Quality {GetValue(values, "volatilitySpread.signalQuality")}");
            }

            if (notes.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Notes");
                lines.AddRange(notes);
            }

            return string.Join("\n", lines);
        }

        private static string GetValue(Dictionary<string, string> values, string key)
            => values.TryGetValue(key, out var value) ? value : "0";

        private static double GetDouble(Dictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out var raw))
            {
                return 0;
            }
            return ParseHelper.ParseDouble(raw);
        }

        private static string FormatPercentValue(double value, int decimals)
        {
            return FormatRounded(value, decimals) + "%";
        }

        private static string FormatRounded(double value, int decimals)
        {
            var format = "0." + new string('#', Math.Max(0, decimals));
            var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            var formatted = rounded.ToString(format, CultureInfo.InvariantCulture);
            return formatted.EndsWith(".", StringComparison.Ordinal) ? formatted.TrimEnd('.') : formatted;
        }

        public static string FormatDouble(double value)
        {
            return ParseHelper.FormatDouble(value);
        }
    }
}
