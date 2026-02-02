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

        /// <summary>
        /// Calculates the velocity of correlation change by computing rolling correlations
        /// and measuring the rate of change. Useful for detecting regime changes.
        /// </summary>
        /// <param name="returnsPrimary">Primary returns series</param>
        /// <param name="returnsSecondary">Secondary returns series</param>
        /// <param name="windowSize">Size of rolling window for correlation</param>
        /// <param name="velocityLookback">How many windows to compare for velocity</param>
        /// <returns>Correlation velocity result with current, previous correlation and velocity</returns>
        public static CorrelationVelocityResult CalculateCorrelationVelocity(
            IReadOnlyList<double> returnsPrimary,
            IReadOnlyList<double> returnsSecondary,
            int windowSize = 50,
            int velocityLookback = 10)
        {
            var count = Math.Min(returnsPrimary.Count, returnsSecondary.Count);
            
            if (count < windowSize + velocityLookback)
            {
                // Not enough data, return current correlation with zero velocity
                var currentCorr = PearsonCorrelation(returnsPrimary, returnsSecondary);
                return new CorrelationVelocityResult(currentCorr, currentCorr, 0, 0, "stable");
            }

            // Calculate rolling correlations
            var rollingCorrelations = new List<double>();
            for (var i = windowSize; i <= count; i++)
            {
                var startIdx = i - windowSize;
                var windowPrimary = new List<double>(windowSize);
                var windowSecondary = new List<double>(windowSize);
                
                for (var j = startIdx; j < i; j++)
                {
                    windowPrimary.Add(returnsPrimary[j]);
                    windowSecondary.Add(returnsSecondary[j]);
                }
                
                rollingCorrelations.Add(PearsonCorrelation(windowPrimary, windowSecondary));
            }

            if (rollingCorrelations.Count < velocityLookback + 1)
            {
                var currentCorr = rollingCorrelations.Count > 0 ? rollingCorrelations[rollingCorrelations.Count - 1] : 0;
                return new CorrelationVelocityResult(currentCorr, currentCorr, 0, 0, "stable");
            }

            var currentCorrelation = rollingCorrelations[rollingCorrelations.Count - 1];
            var previousCorrelation = rollingCorrelations[rollingCorrelations.Count - 1 - velocityLookback];
            
            // Velocity = change per period
            var velocity = (currentCorrelation - previousCorrelation) / velocityLookback;
            
            // Calculate acceleration by looking at velocity change
            var acceleration = 0.0;
            if (rollingCorrelations.Count >= 2 * velocityLookback + 1)
            {
                var previousVelocity = (rollingCorrelations[rollingCorrelations.Count - 1 - velocityLookback] 
                                       - rollingCorrelations[rollingCorrelations.Count - 1 - 2 * velocityLookback]) / velocityLookback;
                acceleration = velocity - previousVelocity;
            }

            // Determine regime
            var regime = DetermineCorrelationRegime(currentCorrelation, velocity, previousCorrelation);

            return new CorrelationVelocityResult(currentCorrelation, previousCorrelation, velocity, acceleration, regime);
        }

        private static string DetermineCorrelationRegime(double current, double velocity, double previous)
        {
            const double strongThreshold = 0.7;
            const double weakThreshold = 0.3;
            const double velocityThreshold = 0.01;

            if (Math.Abs(velocity) > velocityThreshold)
            {
                if (velocity > 0 && current > previous)
                    return current >= strongThreshold ? "strengthening" : "recovering";
                if (velocity < 0 && current < previous)
                    return current <= weakThreshold ? "breaking_down" : "weakening";
            }

            if (Math.Abs(current) >= strongThreshold)
                return "stable_strong";
            if (Math.Abs(current) <= weakThreshold)
                return "stable_weak";
            
            return "stable";
        }

        /// <summary>
        /// Calculates volatility-adjusted spread, normalizing spread by combined volatility.
        /// High spread with low volatility = stronger signal.
        /// </summary>
        /// <param name="primary">Primary price series</param>
        /// <param name="secondary">Secondary price series</param>
        /// <param name="lookbackPeriod">Period for volatility calculation</param>
        /// <returns>Volatility-adjusted spread result</returns>
        public static VolatilityAdjustedSpreadResult CalculateVolatilityAdjustedSpread(
            List<double> primary,
            List<double> secondary,
            int lookbackPeriod = 20)
        {
            var count = Math.Min(primary.Count, secondary.Count);
            
            if (count < 2)
            {
                return new VolatilityAdjustedSpreadResult(0, 0, 0, 0, 0, 0, "insufficient_data");
            }

            // Calculate returns for volatility
            var returnsPrimary = CalculateReturns(primary);
            var returnsSecondary = CalculateReturns(secondary);

            // Calculate current volatility (standard deviation of returns)
            var volPeriod = Math.Min(lookbackPeriod, returnsPrimary.Count);
            var recentPrimaryReturns = new List<double>(volPeriod);
            var recentSecondaryReturns = new List<double>(volPeriod);
            
            for (var i = returnsPrimary.Count - volPeriod; i < returnsPrimary.Count; i++)
            {
                recentPrimaryReturns.Add(returnsPrimary[i]);
                recentSecondaryReturns.Add(returnsSecondary[i]);
            }

            var volPrimary = Std(recentPrimaryReturns, Mean(recentPrimaryReturns));
            var volSecondary = Std(recentSecondaryReturns, Mean(recentSecondaryReturns));
            
            // Combined volatility (root mean square)
            var combinedVolatility = Math.Sqrt((volPrimary * volPrimary + volSecondary * volSecondary) / 2);
            
            // Calculate spread
            var spread = CalculateSpread(primary, secondary);
            var currentSpread = spread[spread.Count - 1];
            var spreadMean = Mean(spread);
            var spreadStd = Std(spread, spreadMean);
            
            // Standard Z-score
            var rawZScore = spreadStd > 0 ? (currentSpread - spreadMean) / spreadStd : 0;
            
            // Volatility-adjusted Z-score: amplify signal when volatility is low
            // Higher combined volatility = divide more = smaller signal (noise)
            // Lower combined volatility = divide less = larger signal (stronger)
            var volatilityFactor = combinedVolatility > PriceEpsilon ? 1.0 / (1.0 + combinedVolatility * 10) : 1.0;
            var adjustedZScore = rawZScore * (1.0 + volatilityFactor);
            
            // Calculate signal strength (0-100)
            var signalStrength = Clamp(Math.Abs(adjustedZScore) / 3.0 * 100.0, 0, 100);
            
            // Determine signal quality
            var signalQuality = DetermineSignalQuality(adjustedZScore, rawZScore, combinedVolatility);

            return new VolatilityAdjustedSpreadResult(
                rawZScore, 
                adjustedZScore, 
                combinedVolatility, 
                volPrimary, 
                volSecondary, 
                signalStrength, 
                signalQuality);
        }

        private static string DetermineSignalQuality(double adjustedZ, double rawZ, double volatility)
        {
            var absAdjusted = Math.Abs(adjustedZ);
            var absRaw = Math.Abs(rawZ);

            // High spread, low volatility = premium signal
            if (absAdjusted >= 2.0 && volatility < 0.02)
                return "premium";
            
            // Good signal with reasonable volatility
            if (absAdjusted >= 1.5 && volatility < 0.04)
                return "strong";
            
            // Moderate signal
            if (absAdjusted >= 1.0)
                return "moderate";
            
            // High volatility makes signal unreliable
            if (volatility > 0.05)
                return "noisy";
            
            return "weak";
        }
    }
}
