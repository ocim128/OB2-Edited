using DeviceId;
using RuriLib.Attributes;
using RuriLib.Functions.Parsing;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Services.Modem;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TextCopy;

namespace RuriLib.Blocks.Utility
{
    [BlockCategory("Utility", "Utility blocks for miscellaneous purposes", "#fad6a5")]
    public static class Methods
    {
        [Block("Clears the cookie jar used for HTTP requests")]
        public static void ClearCookies(BotData data)
        {
            data.COOKIES = new();
            data.TlsClientSessionId = null;
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
            var regexTimeoutLogged = false;

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
                        try
                        {
                            isMatch = RegexCache.GetOrCreate(pattern, regexOptions).IsMatch(text);
                        }
                        catch (RegexMatchTimeoutException ex)
                        {
                            if (!regexTimeoutLogged)
                            {
                                data.Logger.LogError($"Clipboard regex timed out: {ex.Message}", ex);
                                regexTimeoutLogged = true;
                            }
                        }
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

        [Block("Runs the modem automation to toggle network preferences and refresh the IP", name = "Refresh Modem IP")]
        public static async Task<bool> RefreshModemIp(BotData data, [Interpolated] string routerAddress = "http://192.168.0.1", [Interpolated] string username = "admin", [Interpolated] string password = "admin")
        {
            data.Logger.LogHeader();

            var service = new ModemRefreshService();

            try
            {
                var result = await service.RefreshAsync(new ModemRefreshRequest
                {
                    RouterAddress = routerAddress ?? string.Empty,
                    Username = string.IsNullOrWhiteSpace(username) ? "admin" : username,
                    Password = string.IsNullOrWhiteSpace(password) ? "admin" : password
                }, data.CancellationToken).ConfigureAwait(false);

                foreach (var entry in result.Logs)
                {
                    data.Logger.Log(entry, LogColors.SlateGray);
                }

                var color = result.IsSuccess ? LogColors.SpringGreen : LogColors.Crimson;
                data.Logger.Log(result.StatusMessage, color);
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                data.Logger.LogError($"Modem refresh failed: {ex.Message}", ex);
                return false;
            }
        }

        [Block("Generates a 6-digit OTP code from a Base32 secret", name = "2FA Solver")]
        public static string TwoFactorSolver(BotData data, [Variable] string secret)
        {
            data.Logger.LogHeader();

            if (string.IsNullOrWhiteSpace(secret))
            {
                data.Logger.Log("Secret is empty, returning empty OTP", LogColors.DeepChampagne);
                return string.Empty;
            }

            try
            {
                var normalized = TwoFactorUtility.NormalizeSecret(secret);
                if (string.IsNullOrEmpty(normalized))
                {
                    data.Logger.Log("Secret is empty after normalization, returning empty OTP", LogColors.DeepChampagne);
                    return string.Empty;
                }

                if (TwoFactorUtility.TryGenerateOtp(normalized, DateTime.UtcNow, out var otp, out _, out var error))
                {
                    data.Logger.Log($"Generated OTP {otp}", LogColors.DeepChampagne);
                    return otp;
                }

                var failureMessage = string.IsNullOrWhiteSpace(error) ? "Unable to generate OTP." : error;
                data.Logger.LogError($"Failed to generate OTP: {failureMessage}", new InvalidOperationException(failureMessage));
            }
            catch (Exception ex)
            {
                data.Logger.LogError($"Failed to generate OTP: {ex.Message}", ex);
            }

            return string.Empty;
        }
    }

    public static class TwoFactorUtility
    {
        public const int TotpPeriodSeconds = 30;
        private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static string NormalizeSecret(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(input.Length);
            foreach (var c in input.ToUpperInvariant())
            {
                if (char.IsWhiteSpace(c) || c == '-')
                {
                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        public static bool TryGenerateOtp(string base32Secret, DateTime timestampUtc, out string otp, out int secondsRemaining, out string error)
        {
            otp = "------";
            secondsRemaining = 0;
            error = string.Empty;

            try
            {
                var keyBytes = DecodeBase32(base32Secret);
                if (keyBytes.Length == 0)
                {
                    error = "Secret decodes to an empty value.";
                    return false;
                }

                var unixSeconds = (long)Math.Floor((timestampUtc - Epoch).TotalSeconds);
                secondsRemaining = TotpPeriodSeconds - (int)(unixSeconds % TotpPeriodSeconds);
                if (secondsRemaining == TotpPeriodSeconds)
                {
                    secondsRemaining = TotpPeriodSeconds;
                }

                var timestep = unixSeconds / TotpPeriodSeconds;
                var hotp = ComputeHotp(keyBytes, timestep);
                otp = hotp.ToString("000000");
                return true;
            }
            catch (FormatException ex)
            {
                error = ex.Message;
            }
            catch (Exception ex)
            {
                error = $"Failed to generate OTP: {ex.Message}";
            }

            return false;
        }

        private static int ComputeHotp(byte[] key, long counter)
        {
            var counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(counterBytes);
            }

            using var hmac = new HMACSHA1(key);
            var hash = hmac.ComputeHash(counterBytes);
            var offset = hash[^1] & 0x0F;

            var binaryCode = ((hash[offset] & 0x7F) << 24)
                | ((hash[offset + 1] & 0xFF) << 16)
                | ((hash[offset + 2] & 0xFF) << 8)
                | (hash[offset + 3] & 0xFF);

            return binaryCode % 1_000_000;
        }

        private static byte[] DecodeBase32(string input)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

            var sanitized = new List<int>(input.Length);
            foreach (var c in input)
            {
                if (c == '=')
                {
                    break;
                }

                var index = alphabet.IndexOf(c);
                if (index < 0)
                {
                    throw new FormatException($"Invalid Base32 character '{c}'.");
                }

                sanitized.Add(index);
            }

            var output = new List<byte>((sanitized.Count * 5) / 8);
            var bitBuffer = 0;
            var bitsLeft = 0;

            foreach (var value in sanitized)
            {
                bitBuffer = (bitBuffer << 5) | value;
                bitsLeft += 5;

                if (bitsLeft >= 8)
                {
                    bitsLeft -= 8;
                    output.Add((byte)((bitBuffer >> bitsLeft) & 0xFF));
                }
            }

            return output.ToArray();
        }
    }
}
