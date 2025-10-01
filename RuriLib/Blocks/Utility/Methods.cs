using DeviceId;
using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TextCopy;
using System.Text.RegularExpressions;

namespace RuriLib.Blocks.Utility
{
    [BlockCategory("Utility", "Utility blocks for miscellaneous purposes", "#fad6a5")]
    public static class Methods
    {
        [Block("Clears the cookie jar used for HTTP requests")]
        public static void ClearCookies(BotData data)
        {
            data.COOKIES = new();
            data.Logger.LogHeader();
            data.Logger.Log($"Cleared the HTTP cookie jar", LogColors.DeepChampagne);
        }

        [Block("Sleeps for a specified amount of milliseconds")]
        public static async Task Delay(BotData data, int milliseconds)
        {
            data.Logger.LogHeader();
            await Task.Delay(milliseconds, data.CancellationToken).ConfigureAwait(false);
            data.Logger.Log($"Waited {milliseconds} ms", LogColors.DeepChampagne);
        }

        [Block("Retrieves a unique hardware ID for the current machine", name = "Get HWID")]
        public static string GetHWID(BotData data)
        {
            var builder = new DeviceIdBuilder()
                .AddUserName()
                .AddMachineName()
                .AddOSVersion()
                .AddMacAddress()
                .AddSystemDriveSerialNumber()
                .AddOSInstallationID();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                builder
                    .AddProcessorId()
                    .AddMotherboardSerialNumber()
                    .AddSystemUUID();
            }

            var hwid = builder.ToString();

            data.Logger.LogHeader();
            data.Logger.Log($"Got HWID {hwid}", LogColors.DeepChampagne);
            return hwid;
        }

        [Block("Gets text from the system clipboard", name = "Get Clipboard")]
        public static string GetClipboard(BotData data)
        {
            data.Logger.LogHeader();

            try
            {
                var text = ClipboardService.GetText() ?? string.Empty;
                var preview = text.Length <= 64 ? text : text.Substring(0, 64) + "...";
                data.Logger.Log($"Got clipboard text ({text.Length} chars): '{preview}'", LogColors.DeepChampagne);
                return text;
            }
            catch (Exception ex)
            {
                data.Logger.LogError($"Failed to get clipboard text: {ex.Message}", ex);
                return string.Empty;
            }
        }

        [Block("Sets the system clipboard text", name = "Set Clipboard")]
        public static void SetClipboard(BotData data, [Variable] string text)
        {
            data.Logger.LogHeader();

            try
            {
                ClipboardService.SetText(text ?? string.Empty);
                var preview = (text ?? string.Empty);
                preview = preview.Length <= 64 ? preview : preview.Substring(0, 64) + "...";
                data.Logger.Log($"Set clipboard text ({(text?.Length ?? 0)} chars): '{preview}'", LogColors.DeepChampagne);
            }
            catch (Exception ex)
            {
                data.Logger.LogError($"Failed to set clipboard text: {ex.Message}", ex);
            }
        }

        [Block("Clears the system clipboard content", name = "Clear Clipboard")]
        public static void ClearClipboard(BotData data)
        {
            data.Logger.LogHeader();
            try
            {
                ClipboardService.SetText(string.Empty);
                data.Logger.Log("Cleared the system clipboard", LogColors.DeepChampagne);
            }
            catch (Exception ex)
            {
                data.Logger.LogError($"Failed to clear clipboard: {ex.Message}", ex);
            }
        }

        [Block("Waits until clipboard text matches a pattern and returns it", name = "Wait Clipboard")]
        public static async Task<string> WaitClipboard(BotData data, string pattern, int timeoutMs = 5000, bool useRegex = false, bool caseSensitive = false, int pollEveryMs = 100)
        {
            data.Logger.LogHeader();

            var start = DateTime.UtcNow;
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var regexOptions = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

            while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
            {
                try
                {
                    var text = ClipboardService.GetText() ?? string.Empty;
                    var isMatch = false;

                    if (string.IsNullOrEmpty(pattern))
                    {
                        isMatch = text.Length > 0; // if no pattern provided, any non-empty text
                    }
                    else if (useRegex)
                    {
                        isMatch = Regex.IsMatch(text, pattern, regexOptions);
                    }
                    else
                    {
                        isMatch = text.IndexOf(pattern, comparison) >= 0;
                    }

                    if (isMatch)
                    {
                        var preview = text.Length <= 64 ? text : text.Substring(0, 64) + "...";
                        data.Logger.Log($"Clipboard matched pattern. Returning text ({text.Length} chars): '{preview}'", LogColors.DeepChampagne);
                        return text;
                    }
                }
                catch (Exception ex)
                {
                    data.Logger.LogError($"Clipboard check failed: {ex.Message}", ex);
                }

                await Task.Delay(pollEveryMs, data.CancellationToken).ConfigureAwait(false);
            }

            data.Logger.Log($"Clipboard did not match within {timeoutMs} ms, returning empty", LogColors.DeepChampagne);
            return string.Empty;
        }

        [Block("Sleeps for a random amount of milliseconds between two values", name = "Random Delay")]
        public static async Task RandomDelay(BotData data, int minMilliseconds, int maxMilliseconds)
        {
            if (minMilliseconds < 0) minMilliseconds = 0;
            if (maxMilliseconds < minMilliseconds) maxMilliseconds = minMilliseconds;

            var delay = data.Random.Next(minMilliseconds, maxMilliseconds + 1);
            data.Logger.LogHeader();
            await Task.Delay(delay, data.CancellationToken).ConfigureAwait(false);
            data.Logger.Log($"Waited randomly for {delay} ms (range {minMilliseconds}-{maxMilliseconds})", LogColors.DeepChampagne);
        }
    }
}
