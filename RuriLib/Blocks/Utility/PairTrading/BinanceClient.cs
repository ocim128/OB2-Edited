using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Utility.PairTrading
{
    /// <summary>
    /// HTTP client for fetching data from Binance API.
    /// </summary>
    internal static class BinanceClient
    {
        private const int MaxBinanceLimit = 1000;
        private const int DefaultHttpTimeoutMs = 15000;

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        public static async Task<string> FetchKlinesPaged(
            BotData data,
            string symbol,
            string interval,
            int totalBars,
            int batchSize,
            string endTimeMs,
            int delayMs,
            int timeoutMs,
            string baseUrl)
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
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken);
                    if (timeoutMs > 0)
                    {
                        timeoutCts.CancelAfter(timeoutMs);
                    }

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    var response = await HttpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (TaskCanceledException ex) when (!data.CancellationToken.IsCancellationRequested)
                {
                    data.Logger.LogError($"Timed out after {timeoutMs} ms fetching klines.", ex);
                    break;
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

        private static bool TryExtractKlines(string json, out List<string> klines, out long firstOpenTime)
        {
            klines = [];
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
    }
}
