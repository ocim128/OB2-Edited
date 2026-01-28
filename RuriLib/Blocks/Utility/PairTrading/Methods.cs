using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;

namespace RuriLib.Blocks.Utility.PairTrading
{
    [BlockCategory("Pair Trading", "Blocks for analyzing relationships between two trading pairs", "#fad6a5")]
    public static class Methods
    {
        private const double PriceEpsilon = 1e-12;
        private const float DefaultTailThreshold = 0.95f;
        private const int DefaultMaxBars = 12000;
        private const int MaxBinanceLimit = 1000;
        private static readonly HttpClient HttpClient = new();

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

            var primaryValues = ParseSeries(primaryCloses, out var primaryInvalid);
            var secondaryValues = ParseSeries(secondaryCloses, out var secondaryInvalid);

            var aligned = AlignSeries(primaryValues, secondaryValues, out var droppedCount);
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
                return BuildEmptyResult(primaryValues.Count, secondaryValues.Count, primaryInvalid, secondaryInvalid, alignedPrimary.Count, droppedCount);
            }

            var returnsPrimary = CalculateReturns(alignedPrimary);
            var returnsSecondary = CalculateReturns(alignedSecondary);
            var correlation = PearsonCorrelation(returnsPrimary, returnsSecondary);

            var spread = CalculateSpread(alignedPrimary, alignedSecondary);
            var ratioSeries = CalculateRatio(alignedPrimary, alignedSecondary);

            var spreadMean = Mean(spread);
            var spreadStd = Std(spread, spreadMean);
            var spreadZ = spreadStd > 0
                ? (spread[spread.Count - 1] - spreadMean) / spreadStd
                : 0;

            var ratio = ratioSeries.Count > 0 ? ratioSeries[ratioSeries.Count - 1] : 0;

            CopulaResult? copula = null;
            WaveletResult? wavelet = null;
            TransferEntropyResult? entropy = null;

            if (computeCopula)
            {
                copula = CalculateCopulaDependence(returnsPrimary, returnsSecondary, copulaWindow, copulaTailThreshold);
            }

            if (computeWavelet)
            {
                wavelet = WaveletDecompose(spread, waveletType, waveletLevels);
            }

            if (computeTransferEntropy)
            {
                entropy = CalculateTransferEntropy(returnsPrimary, returnsSecondary, transferEntropyHistory, transferEntropyBins);
            }

            var methodScores = new List<double>();
            if (copula.HasValue) methodScores.Add(copula.Value.OpportunityScore);
            if (wavelet.HasValue) methodScores.Add(ScoreWavelet(wavelet.Value));
            if (entropy.HasValue) methodScores.Add(ScoreTransferEntropy(entropy.Value));

            var methodAverage = methodScores.Count > 0 ? Mean(methodScores) : 0;
            var spreadOpportunity = Clamp((Math.Min(3, Math.Abs(spreadZ)) / 3.0) * 100.0, 0, 100);
            var overallOpportunity = Clamp(Math.Round(spreadOpportunity * 0.6 + methodAverage * 0.4, MidpointRounding.AwayFromZero), 0, 100);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["primary.count"] = primaryValues.Count.ToString(CultureInfo.InvariantCulture),
                ["secondary.count"] = secondaryValues.Count.ToString(CultureInfo.InvariantCulture),
                ["aligned.count"] = alignedPrimary.Count.ToString(CultureInfo.InvariantCulture),
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
                result["wavelet.signalScore"] = FormatDouble(ScoreWavelet(w));
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
                result["entropy.score"] = FormatDouble(ScoreTransferEntropy(e));
            }

            var notes = BuildNotes(spreadZ, correlation, entropy);
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

            data.Logger.Log($"Pair analysis completed. Opportunity {FormatDouble(overallOpportunity)}% with {alignedPrimary.Count} bars.", LogColors.DeepChampagne);
            return result;
        }

        [Block("Fetches Binance klines with pagination to exceed the 1000 bar limit", name = "Fetch Binance Klines (Paged)")]
        public static async Task<string> FetchBinanceKlinesPaged(
            BotData data,
            [BlockParam("Symbol", "Binance symbol, e.g. DOGEUSDT")] string symbol,
            [BlockParam("Interval", "Kline interval, e.g. 3m")] string interval,
            int totalBars = DefaultMaxBars,
            int batchSize = MaxBinanceLimit,
            string endTimeMs = "",
            int delayMs = 0,
            string baseUrl = "https://api.binance.com")
        {
            data.Logger.LogHeader();

            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(interval))
            {
                data.Logger.Log("Symbol or interval is empty.", LogColors.DeepChampagne);
                return "[]";
            }

            var remaining = Math.Max(0, totalBars);
            if (remaining == 0)
            {
                data.Logger.Log("Requested 0 bars.", LogColors.DeepChampagne);
                return "[]";
            }

            var limit = Math.Clamp(batchSize, 1, MaxBinanceLimit);
            long? endTime = null;
            if (!string.IsNullOrWhiteSpace(endTimeMs) && long.TryParse(endTimeMs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedEnd))
            {
                endTime = parsedEnd;
            }

            var chunks = new List<List<string>>();
            var fetched = 0;

            while (remaining > 0)
            {
                var requestLimit = Math.Min(limit, remaining);
                var url = $"{baseUrl.TrimEnd('/')}/api/v3/klines?symbol={symbol}&interval={interval}&limit={requestLimit}";
                if (endTime.HasValue && endTime.Value > 0)
                {
                    url += $"&endTime={endTime.Value}";
                }

                string responseText;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    var response = await HttpClient.SendAsync(request, data.CancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    data.Logger.LogError($"Failed to fetch klines: {ex.Message}", ex);
                    break;
                }

                if (string.IsNullOrWhiteSpace(responseText))
                {
                    data.Logger.Log("Empty response from Binance.", LogColors.DeepChampagne);
                    break;
                }

                if (!TryExtractKlines(responseText, out var chunk, out var firstOpenTime))
                {
                    data.Logger.Log("Failed to parse Binance klines response.", LogColors.DeepChampagne);
                    break;
                }

                if (chunk.Count == 0)
                {
                    data.Logger.Log("No more klines returned.", LogColors.DeepChampagne);
                    break;
                }

                chunks.Add(chunk);
                fetched += chunk.Count;
                remaining -= chunk.Count;

                if (remaining <= 0)
                {
                    break;
                }

                if (firstOpenTime > 0)
                {
                    endTime = firstOpenTime - 1;
                }
                else
                {
                    break;
                }

                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, data.CancellationToken).ConfigureAwait(false);
                }
            }

            if (chunks.Count == 0)
            {
                data.Logger.Log("No klines collected.", LogColors.DeepChampagne);
                return "[]";
            }

            chunks.Reverse();
            var outputBars = new List<string>(fetched);
            foreach (var chunk in chunks)
            {
                outputBars.AddRange(chunk);
            }

            var output = $"[{string.Join(",", outputBars)}]";
            data.Logger.Log($"Fetched {outputBars.Count} klines for {symbol}.", LogColors.DeepChampagne);
            return output;
        }

        private static Dictionary<string, string> BuildEmptyResult(int primaryCount, int secondaryCount, int primaryInvalid, int secondaryInvalid, int alignedCount, int droppedCount)
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

        private static List<double> ParseSeries(List<string> values, out int invalidCount)
        {
            invalidCount = 0;
            if (values == null)
            {
                return new List<double>();
            }

            if (values.Count == 1)
            {
                var single = values[0] ?? string.Empty;
                var trimmed = single.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    if (TryParseJsonSeries(trimmed, out var jsonSeries, out var jsonInvalid))
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

                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    result.Add(parsed);
                    continue;
                }

                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
                {
                    result.Add(parsed);
                    continue;
                }

                invalidCount++;
                result.Add(double.NaN);
            }

            return result;
        }

        private static bool TryParseJsonSeries(string json, out List<double> series, out int invalidCount)
        {
            series = new List<double>();
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
            catch (Exception)
            {
                return false;
            }

            return series.Count > 0;
        }

        private static bool TryExtractKlines(string json, out List<string> klines, out long firstOpenTime)
        {
            klines = new List<string>();
            firstOpenTime = 0;

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
                    if (element.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    if (firstOpenTime == 0 && element.GetArrayLength() > 0)
                    {
                        var openElement = element[0];
                        if (openElement.ValueKind == JsonValueKind.Number && openElement.TryGetInt64(out var open))
                        {
                            firstOpenTime = open;
                        }
                        else if (openElement.ValueKind == JsonValueKind.String
                            && long.TryParse(openElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var openParsed))
                        {
                            firstOpenTime = openParsed;
                        }
                    }

                    klines.Add(element.GetRawText());
                }
            }
            catch (JsonException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }

            return klines.Count > 0;
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
                return double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    || double.TryParse(str, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
            }

            return false;
        }

        private static bool TryParseDelimitedSeries(string input, out List<double> series, out int invalidCount)
        {
            series = new List<double>();
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
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    || double.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
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

        private static (List<double> Primary, List<double> Secondary) AlignSeries(List<double> primary, List<double> secondary, out int droppedCount)
        {
            var length = Math.Min(primary.Count, secondary.Count);
            var alignedPrimary = new List<double>(length);
            var alignedSecondary = new List<double>(length);
            droppedCount = 0;

            for (var i = 0; i < length; i++)
            {
                var p = primary[i];
                var s = secondary[i];
                if (!IsFinite(p) || !IsFinite(s))
                {
                    droppedCount++;
                    continue;
                }
                alignedPrimary.Add(p);
                alignedSecondary.Add(s);
            }

            return (alignedPrimary, alignedSecondary);
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static List<string> BuildNotes(double spreadZ, double correlation, TransferEntropyResult? entropy)
        {
            var notes = new List<string>();
            var z = spreadZ;
            if (Math.Abs(z) >= 2)
            {
                notes.Add($"Spread Z-score {FormatSigned(z, 2)} sigma: consider mean-reversion entry.");
            }
            else if (Math.Abs(z) >= 1)
            {
                notes.Add($"Spread Z-score {FormatSigned(z, 2)} sigma: divergence building.");
            }
            else
            {
                notes.Add("Spread is near its mean; low divergence right now.");
            }

            if (Math.Abs(correlation) >= 0.7)
            {
                notes.Add($"Returns correlation is strong ({FormatSigned(correlation, 2)}).");
            }
            else if (Math.Abs(correlation) >= 0.4)
            {
                notes.Add($"Returns correlation is moderate ({FormatSigned(correlation, 2)}).");
            }
            else
            {
                notes.Add($"Returns correlation is weak ({FormatSigned(correlation, 2)}).");
            }

            if (entropy.HasValue && Math.Abs(entropy.Value.NetFlow) > 0.1)
            {
                var leader = entropy.Value.LeadingAsset == "primary" ? "Primary" : "Secondary";
                notes.Add($"{leader} leads by ~{entropy.Value.LagBars} bars: monitor for follow-through.");
            }

            return notes;
        }

        private static List<double> CalculateReturns(List<double> closes)
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

        private static List<double> CalculateSpread(List<double> primary, List<double> secondary)
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

        private static List<double> CalculateRatio(List<double> primary, List<double> secondary)
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

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static double Mean(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            double sum = 0;
            for (var i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }
            return sum / values.Count;
        }

        private static double Std(IReadOnlyList<double> values, double avg)
        {
            if (values == null || values.Count == 0) return 0;
            double sum = 0;
            for (var i = 0; i < values.Count; i++)
            {
                var diff = values[i] - avg;
                sum += diff * diff;
            }
            return Math.Sqrt(sum / values.Count);
        }

        private static double PearsonCorrelation(IReadOnlyList<double> a, IReadOnlyList<double> b)
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

        private static CopulaResult CalculateCopulaDependence(
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
                if (!IsFinite(a) || !IsFinite(b)) continue;
                clean1.Add(a);
                clean2.Add(b);
            }

            if (clean1.Count < 5)
            {
                return new CopulaResult(0, 0, 0, "gaussian", 0);
            }

            var window = windowSize > 10 ? Math.Min(windowSize, clean1.Count) : clean1.Count;
            var start = clean1.Count - window;
            var slice1 = clean1.GetRange(start, window);
            var slice2 = clean2.GetRange(start, window);

            var ranks1 = RankData(slice1);
            var ranks2 = RankData(slice2);
            var spearman = Clamp(PearsonCorrelation(ranks1, ranks2), -1, 1);
            var kendallTau = Clamp((2.0 / Math.PI) * Math.Asin(spearman), -1, 1);

            var u1 = ToPseudoObservations(ranks1);
            var u2 = ToPseudoObservations(ranks2);
            var threshold = Clamp(tailThreshold, 0.5, 0.99);
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
                ? Clamp(upperCount / (count * (1 - upperThreshold)), 0, 1)
                : 0;
            var lower = count > 0 && lowerThreshold > 0
                ? Clamp(lowerCount / (count * lowerThreshold), 0, 1)
                : 0;

            var copulaType = "gaussian";
            if (upper - lower > 0.08)
            {
                copulaType = "gumbel";
            }
            else if (lower - upper > 0.08)
            {
                copulaType = "clayton";
            }

            var tailAsymmetry = Math.Abs(upper - lower);
            var opportunityScore = Clamp(
                (1 - Math.Abs(kendallTau)) * 60 + Clamp(tailAsymmetry, 0, 1) * 40,
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
            if (n == 0) return Array.Empty<double>();
            var denom = n + 1.0;
            var result = new double[n];
            for (var i = 0; i < n; i++)
            {
                result[i] = ranks[i] / denom;
            }
            return result;
        }

        private static WaveletResult WaveletDecompose(List<double> spread, WaveletType waveletType, int maxLevels)
        {
            var clean = new List<double>();
            for (var i = 0; i < spread.Count; i++)
            {
                var value = spread[i];
                if (!IsFinite(value)) continue;
                clean.Add(value);
            }

            if (clean.Count < 4)
            {
                return new WaveletResult(0, 0, 0);
            }

            var originalLength = clean.Count;
            var levels = new List<WaveletLevel>();
            var current = new List<double>(clean);
            var totalEnergy = Math.Max(1e-12, SumSquares(current));
            var depth = Math.Max(1, maxLevels);
            var filters = GetWaveletFilters(waveletType);

            for (var level = 0; level < depth; level++)
            {
                if (current.Count < 2) break;

                var approximation = Dwt(current, filters.LoD, filters.HiD, out var detail);
                var detailEnergy = SumSquares(detail);

                levels.Add(new WaveletLevel
                {
                    Scale = (int)Math.Pow(2, level + 1),
                    Approximation = approximation,
                    Detail = detail,
                    Energy = detailEnergy / totalEnergy
                });

                current = new List<double>(approximation);
            }

            var reconstruction = levels.Count > 0
                ? new List<double>(levels[levels.Count - 1].Approximation)
                : new List<double>(clean);
            for (var i = levels.Count - 1; i >= 0; i--)
            {
                var zeros = new double[reconstruction.Count];
                var reconstructed = Idwt(reconstruction.ToArray(), zeros, filters.LoR, filters.HiR);
                reconstruction = new List<double>(reconstructed);
            }

            var smoothed = reconstruction.GetRange(0, Math.Min(originalLength, reconstruction.Count));

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
            noiseRatio = Clamp(noiseRatio, 0, 1);

            var avg = Mean(smoothed);
            var deviation = Std(smoothed, avg);
            var spreadZ = deviation > 0 ? (smoothed[smoothed.Count - 1] - avg) / deviation : 0;

            return new WaveletResult(dominantCycle, noiseRatio, spreadZ);
        }

        private static double SumSquares(IReadOnlyList<double> values)
        {
            double sum = 0;
            for (var i = 0; i < values.Count; i++)
            {
                sum += values[i] * values[i];
            }
            return sum;
        }

        private static WaveletFilters GetWaveletFilters(WaveletType type)
        {
            double[] loD;
            if (type == WaveletType.Haar)
            {
                var sqrt = Math.Sqrt(0.5);
                loD = new[] { sqrt, sqrt };
            }
            else if (type == WaveletType.Coif2)
            {
                loD = new[]
                {
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
                };
            }
            else
            {
                loD = new[]
                {
                    0.4829629131445341,
                    0.8365163037378079,
                    0.2241438680420134,
                    -0.1294095225512604
                };
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
                return Array.Empty<double>();
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

        private static TransferEntropyResult CalculateTransferEntropy(
            IReadOnlyList<double> returns1,
            IReadOnlyList<double> returns2,
            int historyLength,
            int bins)
        {
            var clean1 = new List<double>();
            var clean2 = new List<double>();
            var length = Math.Min(returns1.Count, returns2.Count);
            for (var i = 0; i < length; i++)
            {
                var a = returns1[i];
                var b = returns2[i];
                if (!IsFinite(a) || !IsFinite(b)) continue;
                clean1.Add(a);
                clean2.Add(b);
            }

            if (clean1.Count < 6)
            {
                return new TransferEntropyResult(0, 0, 0, "neutral", 0, 0);
            }

            var effectiveHistory = Math.Max(1, historyLength);
            var effectiveBins = Math.Max(2, bins);

            var thresholds1 = QuantileThresholds(clean1, effectiveBins);
            var thresholds2 = QuantileThresholds(clean2, effectiveBins);
            var bins1 = Discretize(clean1, thresholds1);
            var bins2 = Discretize(clean2, thresholds2);

            var te1to2 = ComputeTransferEntropyK(bins1, bins2, effectiveBins, effectiveHistory);
            var te2to1 = ComputeTransferEntropyK(bins2, bins1, effectiveBins, effectiveHistory);

            var denom = te1to2 + te2to1 + 1e-9;
            var netFlow = Clamp((te1to2 - te2to1) / denom, -1, 1);

            var leadingAsset = "neutral";
            if (Math.Abs(netFlow) > 0.1)
            {
                leadingAsset = netFlow > 0 ? "primary" : "secondary";
            }

            var lagInfo = EstimateLag(clean1, clean2, 10);
            var significance = Clamp((te1to2 + te2to1) / 0.5, 0, 1);

            return new TransferEntropyResult(te1to2, te2to1, netFlow, leadingAsset, lagInfo.Lag, significance);
        }

        private static double[] QuantileThresholds(List<double> values, int bins)
        {
            if (values.Count == 0) return Array.Empty<double>();
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

        private static double ComputeTransferEntropyK(int[] xBins, int[] yBins, int bins, int k)
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
                return ComputeTransferEntropy(xBins, yBins, bins);
            }

            var stateCount = (int)Math.Pow(bins, effectiveK);
            var total = n - effectiveK;
            var count3 = new Dictionary<long, int>();
            var countYX = new Dictionary<long, int>();
            var countYtY = new Dictionary<long, int>();
            var countY = new Dictionary<long, int>();

            for (var t = effectiveK; t < n; t++)
            {
                var yState = 0;
                var xState = 0;
                for (var i = 0; i < effectiveK; i++)
                {
                    yState = yState * bins + yBins[t - 1 - i];
                    xState = xState * bins + xBins[t - 1 - i];
                }

                var yt = yBins[t];
                var key3 = ((long)yt * stateCount + yState) * stateCount + xState;
                var keyYX = (long)yState * stateCount + xState;
                var keyYtY = (long)yt * stateCount + yState;

                count3[key3] = (count3.TryGetValue(key3, out var v3) ? v3 : 0) + 1;
                countYX[keyYX] = (countYX.TryGetValue(keyYX, out var vyx) ? vyx : 0) + 1;
                countYtY[keyYtY] = (countYtY.TryGetValue(keyYtY, out var vyt) ? vyt : 0) + 1;
                countY[yState] = (countY.TryGetValue(yState, out var vy) ? vy : 0) + 1;
            }

            var alpha = 1e-6;
            var te = 0.0;
            foreach (var kvp in count3)
            {
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

        private static double ComputeTransferEntropy(int[] xBins, int[] yBins, int bins)
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

        private static (int Lag, double Correlation) EstimateLag(List<double> returns1, List<double> returns2, int maxLag)
        {
            var n = Math.Min(returns1.Count, returns2.Count);
            if (n < 5) return (0, 0);

            var bestLag = 0;
            var bestCorr = 0.0;

            for (var lag = -maxLag; lag <= maxLag; lag++)
            {
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
                var corr = PearsonCorrelation(xs, ys);
                if (Math.Abs(corr) > Math.Abs(bestCorr))
                {
                    bestCorr = corr;
                    bestLag = lag;
                }
            }

            return (Math.Abs(bestLag), bestCorr);
        }

        private static double ScoreWavelet(WaveletResult wavelet)
        {
            var z = Math.Min(3, Math.Abs(wavelet.SpreadZScore));
            var signalClarity = Clamp(1 - wavelet.NoiseRatio, 0, 1);
            return Clamp((z / 3) * 60 + signalClarity * 40, 0, 100);
        }

        private static double ScoreTransferEntropy(TransferEntropyResult entropy)
        {
            var flow = Clamp(Math.Abs(entropy.NetFlow), 0, 1);
            var strength = Clamp((entropy.Te1To2 + entropy.Te2To1) / 0.2, 0, 1);
            return Clamp(flow * 70 + strength * 30, 0, 100);
        }

        private static string BuildReport(Dictionary<string, string> values, List<string> notes)
        {
            var lines = new List<string>
            {
                "Overview",
                $"Opportunity {FormatPercentValue(ParseDouble(values, "opportunity.score"), 0)}",
                $"Correlation {FormatSigned(ParseDouble(values, "correlation"), 3)}",
                $"Spread Z {FormatSigned(ParseDouble(values, "spread.zscore"), 2)}",
                $"Ratio {FormatRounded(ParseDouble(values, "ratio"), 6)}",
                $"Aligned Bars {FormatRounded(ParseDouble(values, "aligned.count"), 0)}"
            };

            if (values.ContainsKey("copula.kendallTau"))
            {
                lines.Add(string.Empty);
                lines.Add("Copula Dependence");
                lines.Add($"Kendall Tau {FormatRounded(ParseDouble(values, "copula.kendallTau"), 3)}");
                lines.Add($"Upper Tail {FormatRounded(ParseDouble(values, "copula.tailUpper"), 2)}");
                lines.Add($"Lower Tail {FormatRounded(ParseDouble(values, "copula.tailLower"), 2)}");
                lines.Add($"Copula Type {GetValue(values, "copula.type")}");
                lines.Add($"Opportunity {FormatPercentValue(ParseDouble(values, "copula.opportunityScore"), 0)}");
            }

            if (values.ContainsKey("wavelet.dominantCycle"))
            {
                lines.Add(string.Empty);
                lines.Add("Wavelet Signals");
                lines.Add($"Dominant Cycle {FormatRounded(ParseDouble(values, "wavelet.dominantCycle"), 0)} bars");
                lines.Add($"Noise Ratio {FormatPercent(ParseDouble(values, "wavelet.noiseRatio"))}");
                lines.Add($"Spread Z {FormatSigned(ParseDouble(values, "wavelet.spreadZScore"), 2)}");
                lines.Add($"Signal Score {FormatPercentValue(ParseDouble(values, "wavelet.signalScore"), 0)}");
            }

            if (values.ContainsKey("entropy.te1to2"))
            {
                lines.Add(string.Empty);
                lines.Add("Transfer Entropy");
                lines.Add($"TE 1 -> 2 {FormatRounded(ParseDouble(values, "entropy.te1to2"), 4)}");
                lines.Add($"TE 2 -> 1 {FormatRounded(ParseDouble(values, "entropy.te2to1"), 4)}");
                lines.Add($"Net Flow {FormatSigned(ParseDouble(values, "entropy.netFlow"), 2)}");
                lines.Add($"Leader {GetValue(values, "entropy.leadingAsset")}");
                lines.Add($"Lag {FormatRounded(ParseDouble(values, "entropy.lagBars"), 0)} bars");
                lines.Add($"Confidence {FormatPercent(ParseDouble(values, "entropy.significance"))}");
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

        private static double ParseDouble(Dictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out var raw))
            {
                return 0;
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            {
                return parsed;
            }

            return 0;
        }

        private static string FormatPercent(double value)
        {
            var percent = Clamp(value, 0, 1) * 100;
            return Math.Round(percent, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private static string FormatPercentValue(double value, int decimals)
        {
            return FormatRounded(value, decimals) + "%";
        }

        private static string FormatSigned(double value, int decimals)
        {
            var format = "0." + new string('#', Math.Max(0, decimals));
            var formatted = value.ToString(format, CultureInfo.InvariantCulture);
            if (formatted.EndsWith(".", StringComparison.Ordinal))
            {
                formatted = formatted.TrimEnd('.');
            }
            return (value >= 0 ? "+" : string.Empty) + formatted;
        }

        private static string FormatRounded(double value, int decimals)
        {
            var format = "0." + new string('#', Math.Max(0, decimals));
            var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            var formatted = rounded.ToString(format, CultureInfo.InvariantCulture);
            return formatted.EndsWith(".", StringComparison.Ordinal) ? formatted.TrimEnd('.') : formatted;
        }

        private static string FormatDouble(double value)
        {
            if (!IsFinite(value)) return "0";
            var formatted = value.ToString("0.####################", CultureInfo.InvariantCulture);
            return formatted.EndsWith(".", StringComparison.Ordinal) ? formatted.TrimEnd('.') : formatted;
        }

        private readonly struct CopulaResult
        {
            public CopulaResult(double kendallTau, double tailUpper, double tailLower, string copulaType, double opportunityScore)
            {
                KendallTau = kendallTau;
                TailUpper = tailUpper;
                TailLower = tailLower;
                CopulaType = copulaType;
                OpportunityScore = opportunityScore;
            }

            public double KendallTau { get; }
            public double TailUpper { get; }
            public double TailLower { get; }
            public string CopulaType { get; }
            public double OpportunityScore { get; }
        }

        private readonly struct WaveletResult
        {
            public WaveletResult(int dominantCycle, double noiseRatio, double spreadZScore)
            {
                DominantCycle = dominantCycle;
                NoiseRatio = noiseRatio;
                SpreadZScore = spreadZScore;
            }

            public int DominantCycle { get; }
            public double NoiseRatio { get; }
            public double SpreadZScore { get; }
        }

        private readonly struct TransferEntropyResult
        {
            public TransferEntropyResult(double te1To2, double te2To1, double netFlow, string leadingAsset, int lagBars, double significance)
            {
                Te1To2 = te1To2;
                Te2To1 = te2To1;
                NetFlow = netFlow;
                LeadingAsset = leadingAsset;
                LagBars = lagBars;
                Significance = significance;
            }

            public double Te1To2 { get; }
            public double Te2To1 { get; }
            public double NetFlow { get; }
            public string LeadingAsset { get; }
            public int LagBars { get; }
            public double Significance { get; }
        }

        private sealed class WaveletLevel
        {
            public int Scale { get; set; }
            public double[] Approximation { get; set; }
            public double[] Detail { get; set; }
            public double Energy { get; set; }
        }

        private readonly struct WaveletFilters
        {
            public WaveletFilters(double[] loD, double[] hiD, double[] loR, double[] hiR)
            {
                LoD = loD;
                HiD = hiD;
                LoR = loR;
                HiR = hiR;
            }

            public double[] LoD { get; }
            public double[] HiD { get; }
            public double[] LoR { get; }
            public double[] HiR { get; }
        }
    }

    public enum WaveletType
    {
        Haar,
        Db4,
        Coif2
    }
}
