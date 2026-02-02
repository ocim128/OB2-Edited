using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Utility.PairTrading
{
    /// <summary>
    /// Block methods for pair trading analysis.
    /// </summary>
    [BlockCategory("Pair Trading", "Blocks for analyzing relationships between two trading pairs", "#fad6a5")]
    public static class Methods
    {
        private const int DefaultMaxBars = 12000;
        private const float DefaultTailThreshold = 0.95f;

        [Block("Analyzes two price series for pair-trading opportunities", name = "Pair Trading Analysis")]
        public static Dictionary<string, string> AnalyzePairTrading(
            BotData data,
            [Variable][BlockParam("Primary Closes", "List of primary pair close prices aligned by index.")] List<string> primaryCloses,
            [Variable][BlockParam("Secondary Closes", "List of secondary pair close prices aligned by index.")] List<string> secondaryCloses,
            bool computeCopula = true,
            bool computeWavelet = true,
            bool computeTransferEntropy = true,
            int maxBars = DefaultMaxBars,
            WaveletType waveletType = WaveletType.Db4,
            int waveletLevels = 4,
            int copulaWindow = 0,
            float copulaTailThreshold = DefaultTailThreshold,
            int transferEntropyHistory = 2,
            int transferEntropyBins = 8)
        {
            data.Logger.LogHeader();

            var primaryValues = SeriesParser.ParseSeries(data, primaryCloses, out var primaryInvalid);
            var secondaryValues = SeriesParser.ParseSeries(data, secondaryCloses, out var secondaryInvalid);

            var aligned = SeriesParser.AlignSeries(primaryValues, secondaryValues, out var droppedCount);
            var alignedPrimary = aligned.Primary;
            var alignedSecondary = aligned.Secondary;

            if (maxBars > 0 && alignedPrimary.Count > maxBars)
            {
                alignedPrimary = alignedPrimary.GetRange(alignedPrimary.Count - maxBars, maxBars);
                alignedSecondary = alignedSecondary.GetRange(alignedSecondary.Count - maxBars, maxBars);
                data.Logger.Log($"Trimmed to last {maxBars} bars for performance.", LogColors.DeepChampagne);
            }

            if (alignedPrimary.Count < 2)
            {
                data.Logger.Log("Not enough aligned data points to analyze.", LogColors.DeepChampagne);
                return ReportBuilder.BuildEmptyResult(primaryValues.Count, secondaryValues.Count, primaryInvalid, secondaryInvalid, alignedPrimary.Count, droppedCount);
            }

            var returnsPrimary = StatisticalAnalysis.CalculateReturns(alignedPrimary);
            var returnsSecondary = StatisticalAnalysis.CalculateReturns(alignedSecondary);
            var correlation = StatisticalAnalysis.PearsonCorrelation(returnsPrimary, returnsSecondary);

            var spread = StatisticalAnalysis.CalculateSpread(alignedPrimary, alignedSecondary);
            var ratioSeries = StatisticalAnalysis.CalculateRatio(alignedPrimary, alignedSecondary);

            var spreadMean = StatisticalAnalysis.Mean(spread);
            var spreadStd = StatisticalAnalysis.Std(spread, spreadMean);
            var spreadZ = spreadStd > 0
                ? (spread[spread.Count - 1] - spreadMean) / spreadStd
                : 0;

            var ratio = ratioSeries.Count > 0 ? ratioSeries[ratioSeries.Count - 1] : 0;

            CopulaResult? copula = null;
            WaveletResult? wavelet = null;
            TransferEntropyResult? entropy = null;

            if (computeCopula)
            {
                copula = CopulaAnalysis.CalculateCopulaDependence(returnsPrimary, returnsSecondary, copulaWindow, copulaTailThreshold);
            }

            if (computeWavelet)
            {
                wavelet = WaveletAnalysis.WaveletDecompose(spread, waveletType, waveletLevels);
            }

            if (computeTransferEntropy)
            {
                entropy = TransferEntropyAnalysis.CalculateTransferEntropy(returnsPrimary, returnsSecondary, transferEntropyHistory, transferEntropyBins, data.CancellationToken);
            }

            var methodScores = new List<double>();
            if (copula.HasValue) methodScores.Add(copula.Value.OpportunityScore);
            if (wavelet.HasValue) methodScores.Add(WaveletAnalysis.ScoreWavelet(wavelet.Value));
            if (entropy.HasValue) methodScores.Add(TransferEntropyAnalysis.ScoreTransferEntropy(entropy.Value));

            var methodAverage = methodScores.Count > 0 ? StatisticalAnalysis.Mean(methodScores) : 0;
            var spreadOpportunity = StatisticalAnalysis.Clamp((Math.Min(3, Math.Abs(spreadZ)) / 3.0) * 100.0, 0, 100);
            var overallOpportunity = StatisticalAnalysis.Clamp(Math.Round(spreadOpportunity * 0.6 + methodAverage * 0.4, MidpointRounding.AwayFromZero), 0, 100);

            var result = ReportBuilder.BuildResult(
                primaryValues.Count, secondaryValues.Count, primaryInvalid, secondaryInvalid,
                alignedPrimary.Count, droppedCount, correlation, spreadMean, spreadStd, spreadZ,
                ratio, overallOpportunity, spreadOpportunity, methodAverage,
                copula, wavelet, entropy);

            data.Logger.Log($"Pair analysis completed. Opportunity {ReportBuilder.FormatDouble(overallOpportunity)}% with {alignedPrimary.Count} bars.", LogColors.DeepChampagne);
            return result;
        }

        [Block("Fetches Binance klines with pagination to exceed the 1000 bar limit", name = "Fetch Binance Klines (Paged)")]
        public static async Task<string> FetchBinanceKlinesPaged(
            BotData data,
            [BlockParam("Symbol", "Binance symbol, e.g. DOGEUSDT")] string symbol,
            [BlockParam("Interval", "Kline interval, e.g. 3m")] string interval,
            int totalBars = DefaultMaxBars,
            int batchSize = 1000,
            string endTimeMs = "",
            int delayMs = 0,
            int timeoutMs = 15000,
            string baseUrl = "https://api.binance.com")
        {
            return await BinanceClient.FetchKlinesPaged(data, symbol, interval, totalBars, batchSize, endTimeMs, delayMs, timeoutMs, baseUrl);
        }
    }
}
