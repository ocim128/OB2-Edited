using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Playwright;
using Microsoft.Win32;
using OpenBullet2.Native.Infrastructure.DependencyInjection;
using RuriLib.Models.Settings;
using RuriLib.Services;

namespace OpenBullet2.Native.Views.Pages
{
    /// <summary>
    /// Utility tools dashboard.
    /// </summary>
    public partial class Tools : Page
    {
        private const int TotpPeriodSeconds = 30;
        private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly string[] ModemTogglePayloads =
        [
            "isTest=false&goformId=SET_BEARER_PREFERENCE&BearerPreference=Only_LTE%0ALTE_preferred",
            "isTest=false&goformId=SET_BEARER_PREFERENCE&BearerPreference=NETWORK_auto%0ALTE_preferred",
            "isTest=false&goformId=SET_BEARER_PREFERENCE&BearerPreference=Only_LTE%0ALTE_preferred",
            "isTest=false&goformId=SET_BEARER_PREFERENCE&BearerPreference=NETWORK_auto%0ALTE_preferred"
        ];

        private readonly DispatcherTimer timer;
        private string normalizedSecret = string.Empty;
        private string currentOtp = string.Empty;
        private readonly Random modemRandom = new();
        private readonly ObservableCollection<ZipFolderOption> zipOptionFolders = new();
        private readonly List<LaunchedZipProfile> launchedZipProfiles = new();
        private readonly object zipProfileLock = new();
        private string zipArchivePath = string.Empty;
        private bool isLaunchingZip;

        public Tools()
        {
            InitializeComponent();

            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (_, _) => UpdateOtp();
            Unloaded += Tools_Unloaded;

            SetOtpDisplay("------", "Enter a secret key to generate codes.", 0);
            CopyOtpButton.IsEnabled = false;
            BookmarkletStatusBorder.Visibility = Visibility.Collapsed;
            TextCleanerStatusBorder.Visibility = Visibility.Collapsed;
            ZipOptionListBox.ItemsSource = zipOptionFolders;
        }

        private async void Tools_Unloaded(object sender, RoutedEventArgs e)
        {
            timer.Stop();
            await CleanupZipProfilesAsync();
        }

        private void SecretKeyTextChanged(object sender, TextChangedEventArgs e)
        {
            normalizedSecret = NormalizeSecret(SecretKeyTextBox.Text);
            ValidateAndStart();
        }

        private void PasteSecret(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    SecretKeyTextBox.Text = Clipboard.GetText();
                }
            }
            catch (Exception ex)
            {
                SecretErrorTextBlock.Text = $"Clipboard unavailable: {ex.Message}";
                SecretErrorBorder.Visibility = Visibility.Visible;
            }
        }

        private void ClearSecret(object sender, RoutedEventArgs e)
        {
            SecretKeyTextBox.Clear();
        }

        private void CopyOtp(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentOtp) || currentOtp.Contains('-'))
            {
                return;
            }

            try
            {
                Clipboard.SetText(currentOtp);
            }
            catch (Exception ex)
            {
                SecretErrorTextBlock.Text = $"Unable to copy OTP: {ex.Message}";
                SecretErrorBorder.Visibility = Visibility.Visible;
            }
        }

        private void ValidateAndStart()
        {
            if (string.IsNullOrWhiteSpace(normalizedSecret))
            {
                timer.Stop();
                SecretErrorBorder.Visibility = Visibility.Collapsed;
                SetOtpDisplay("------", "Enter a secret key to generate codes.", 0);
                CopyOtpButton.IsEnabled = false;
                return;
            }

            if (TryGenerateOtp(normalizedSecret, DateTime.UtcNow, out var otp, out var secondsRemaining, out var error))
            {
                SecretErrorBorder.Visibility = Visibility.Collapsed;
                SetOtpDisplay(otp, BuildExpiryMessage(secondsRemaining), TotpPeriodSeconds - secondsRemaining);
                CopyOtpButton.IsEnabled = true;
                timer.Start();
            }
            else
            {
                timer.Stop();
                SecretErrorTextBlock.Text = error;
                SecretErrorBorder.Visibility = Visibility.Visible;
                SetOtpDisplay("------", "Invalid secret.", 0);
                CopyOtpButton.IsEnabled = false;
            }
        }

        private void UpdateOtp()
        {
            if (string.IsNullOrEmpty(normalizedSecret))
            {
                timer.Stop();
                return;
            }

            if (TryGenerateOtp(normalizedSecret, DateTime.UtcNow, out var otp, out var secondsRemaining, out var error))
            {
                SetOtpDisplay(otp, BuildExpiryMessage(secondsRemaining), TotpPeriodSeconds - secondsRemaining);
                CopyOtpButton.IsEnabled = true;
            }
            else
            {
                timer.Stop();
                SecretErrorTextBlock.Text = error;
                SecretErrorBorder.Visibility = Visibility.Visible;
                SetOtpDisplay("------", "Invalid secret.", 0);
                CopyOtpButton.IsEnabled = false;
            }
        }

        private static string BuildExpiryMessage(int secondsRemaining)
            => secondsRemaining <= 1
                ? "Expires in 1 second"
                : $"Expires in {secondsRemaining} seconds";

        private void SetOtpDisplay(string otp, string statusMessage, int elapsedSeconds)
        {
            currentOtp = otp;
            OtpTextBlock.Text = otp;
            OtpStatusTextBlock.Text = statusMessage;
            OtpProgressBar.Value = Math.Max(0, Math.Min(TotpPeriodSeconds, elapsedSeconds));
        }

        private static string NormalizeSecret(string input)
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

        private static bool TryGenerateOtp(string base32Secret, DateTime timestampUtc, out string otp, out int secondsRemaining, out string error)
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

        private void ParseBookmarklet(object sender, RoutedEventArgs e)
        {
            var raw = BookmarkletInputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                BookmarkletStatusTextBlock.Text = "Input is empty.";
                BookmarkletStatusTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;
                BookmarkletStatusBorder.Visibility = Visibility.Visible;
                BookmarkletOutputTextBox.Clear();
                return;
            }

            var lines = raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal))
                .ToArray();

            if (lines.Length == 0)
            {
                BookmarkletStatusTextBlock.Text = "No usable lines.";
                BookmarkletStatusTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;
                BookmarkletStatusBorder.Visibility = Visibility.Visible;
                BookmarkletOutputTextBox.Clear();
                return;
            }

            var results = new List<string>();

            foreach (var line in lines)
            {
                var parsed = TryParseBookmarkletLine(line);
                results.Add(parsed);
            }

            BookmarkletOutputTextBox.Text = string.Join(Environment.NewLine + Environment.NewLine, results);
            BookmarkletStatusTextBlock.Text = $"Parsed {lines.Length} line(s).";
            BookmarkletStatusTextBlock.Foreground = System.Windows.Media.Brushes.LawnGreen;
            BookmarkletStatusTextBlock.Visibility = Visibility.Visible;
        }

        private void ClearBookmarklet(object sender, RoutedEventArgs e)
        {
            BookmarkletInputTextBox.Clear();
            BookmarkletOutputTextBox.Clear();
            BookmarkletStatusBorder.Visibility = Visibility.Collapsed;
        }

        private void CopyBookmarkletOutput(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(BookmarkletOutputTextBox.Text))
            {
                return;
            }

            try
            {
                Clipboard.SetText(BookmarkletOutputTextBox.Text);
                BookmarkletStatusTextBlock.Text = "Output copied to clipboard.";
                BookmarkletStatusTextBlock.Foreground = System.Windows.Media.Brushes.LawnGreen;
                BookmarkletStatusBorder.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                BookmarkletStatusTextBlock.Text = $"Unable to copy output: {ex.Message}";
                BookmarkletStatusTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;
                BookmarkletStatusBorder.Visibility = Visibility.Visible;
            }
        }

        private void BookmarkletInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                ParseBookmarklet(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void TextCleanerInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                CleanTextInput(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void CleanTextInput(object sender, RoutedEventArgs e)
        {
            var raw = TextCleanerInputTextBox.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                TextCleanerOutputTextBox.Clear();
                SetTextCleanerStatus("Input is empty.", System.Windows.Media.Brushes.OrangeRed);
                return;
            }

            var cleanedLines = CleanAndSortLines(raw);
            if (cleanedLines.Count == 0)
            {
                TextCleanerOutputTextBox.Clear();
                SetTextCleanerStatus("No valid lines found.", System.Windows.Media.Brushes.OrangeRed);
                return;
            }

            TextCleanerOutputTextBox.Text = string.Join(Environment.NewLine, cleanedLines);
            SetTextCleanerStatus($"Processed {cleanedLines.Count} line(s).", System.Windows.Media.Brushes.LawnGreen);
        }

        private void ClearTextCleaner(object sender, RoutedEventArgs e)
        {
            TextCleanerInputTextBox.Clear();
            TextCleanerOutputTextBox.Clear();
            TextCleanerStatusBorder.Visibility = Visibility.Collapsed;
        }

        private void CopyTextCleanerOutput(object sender, RoutedEventArgs e)
        {
            var output = TextCleanerOutputTextBox.Text;
            if (string.IsNullOrEmpty(output))
            {
                SetTextCleanerStatus("Nothing to copy.", System.Windows.Media.Brushes.OrangeRed);
                return;
            }

            try
            {
                Clipboard.SetText(output);
                SetTextCleanerStatus("Output copied to clipboard.", System.Windows.Media.Brushes.LawnGreen);
            }
            catch (Exception ex)
            {
                SetTextCleanerStatus($"Unable to copy output: {ex.Message}", System.Windows.Media.Brushes.OrangeRed);
            }
        }

        private static List<string> CleanAndSortLines(string raw)
        {
            var lines = raw.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var cleaned = new List<(string Line, int Index)>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var normalized = Regex.Replace(line, " {2,}", " ");
                normalized = normalized.TrimEnd();
                normalized = normalized.Replace("♦️", "♦");
                normalized = normalized.Replace("♠️", "♠");
                normalized = normalized.Replace("♠ ", "♠");

                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                cleaned.Add((normalized, cleaned.Count));
            }

            return cleaned
                .OrderBy(entry => GetTextCleanerSortKey(entry.Line))
                .ThenBy(entry => entry.Index)
                .Select(entry => entry.Line)
                .ToList();
        }

        private static int GetTextCleanerSortKey(string line)
        {
            var match = Regex.Match(line, "♦(\\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var value))
            {
                return value;
            }

            return int.MaxValue;
        }

        private void SetTextCleanerStatus(string message, System.Windows.Media.Brush brush)
        {
            TextCleanerStatusTextBlock.Text = message;
            TextCleanerStatusTextBlock.Foreground = brush;
            TextCleanerStatusBorder.Visibility = Visibility.Visible;
        }

        private static string TryParseBookmarkletLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return "Invalid input";
            }

            var authTokenPattern = ExtractAuthTokenLine(line);
            if (authTokenPattern != null)
            {
                return authTokenPattern;
            }

            var detailedPattern = ExtractDetailedPatternLine(line);
            if (detailedPattern != null)
            {
                return detailedPattern;
            }

            var fallback = ExtractFallbackLine(line);
            if (fallback != null)
            {
                return fallback;
            }

            return "Invalid input";
        }

        private static string? ExtractAuthTokenLine(string line)
        {
            if (!line.Contains("auth_token="))
            {
                return null;
            }

            var emailMatch = Regex.Match(line, "\\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}\\b", RegexOptions.IgnoreCase);
            if (!emailMatch.Success)
            {
                return "Invalid input";
            }

            var email = emailMatch.Value;
            var usernamePart = email.Split('@')[0];
            var emailSuffix = usernamePart.Length >= 3 ? usernamePart[^3..] : null;
            var authTokenMatch = Regex.Match(line, "auth_token=(\\w+)");
            var authToken = authTokenMatch.Success ? authTokenMatch.Groups[1].Value : null;

            var userMatches = Regex.Matches(line, "@\\S+");
            string? username = null;
            if (userMatches.Count > 0)
            {
                var domainPart = email.Split('@')[1];
                foreach (Match match in userMatches)
                {
                    var candidate = match.Value.TrimStart('@');
                    if (!candidate.Equals(domainPart, StringComparison.OrdinalIgnoreCase))
                    {
                        username = candidate;
                    }
                }
            }

            var post = Regex.Match(line, "^(\\d+)♠").Groups[1].Value;
            var follower = Regex.Match(line, "(\\d+)~").Groups[1].Value;
            var year = Regex.Match(line, "~\\s*(\\d+)").Groups[1].Value;

            var passwordLine = emailSuffix != null ? $"charming@{emailSuffix}" : "N/A";

            var builder = new StringBuilder();
            builder.AppendLine(email);
            builder.AppendLine($"password: {passwordLine}");
            builder.AppendLine($"check email: akunlama.com/inbox/{usernamePart}");
            builder.AppendLine($"auth_token={authToken ?? "N/A"}");
            builder.AppendLine();
            builder.Append($"Username•Post•Follower•Tahun = {username ?? "N/A"}•{post}");
            builder.Append($"•{(string.IsNullOrEmpty(follower) ? "N/A" : follower)}");
            builder.Append($"•{(string.IsNullOrEmpty(year) ? "N/A" : year)}");
            return builder.ToString();
        }

        private void RegisterZipProfile(LaunchedZipProfile profile)
        {
            lock (zipProfileLock)
            {
                launchedZipProfiles.Add(profile);
            }

            profile.Context.Close += (_, _) =>
            {
                _ = Dispatcher.InvokeAsync(async () => await OnZipProfileClosedAsync(profile));
            };
        }

        private async Task OnZipProfileClosedAsync(LaunchedZipProfile profile)
        {
            var removed = false;
            lock (zipProfileLock)
            {
                removed = launchedZipProfiles.Remove(profile);
            }

            if (!removed)
            {
                return;
            }

            await CloseZipProfileAsync(profile, closeContext: false);

            SetZipOptionStatus($"Closed Firefox profile '{profile.OptionName}'.", Brushes.LightSteelBlue);
        }

        private async Task CleanupZipProfilesAsync()
        {
            List<LaunchedZipProfile> profiles;
            lock (zipProfileLock)
            {
                if (launchedZipProfiles.Count == 0)
                {
                    return;
                }

                profiles = launchedZipProfiles.ToList();
                launchedZipProfiles.Clear();
            }

            foreach (var profile in profiles)
            {
                await CloseZipProfileAsync(profile, closeContext: true);
            }
        }

        private static async Task CloseZipProfileAsync(LaunchedZipProfile profile, bool closeContext)
        {
            if (closeContext)
            {
                try
                {
                    if (profile.Context != null)
                    {
                        await profile.Context.CloseAsync();
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            try
            {
                profile.Playwright?.Dispose();
            }
            catch
            {
                // Ignore dispose errors
            }

            TryDeleteDirectory(profile.ProfilePath);
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
                // Ignore deletion errors
            }
        }

        private void SelectZipForOptions(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "ZIP archives (*.zip)|*.zip",
                CheckFileExists = true,
                Multiselect = false,
                Title = "Select a ZIP archive"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                using var stream = File.OpenRead(dialog.FileName);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

                var folderNames = CollectTopLevelFolders(archive);

                zipOptionFolders.Clear();
                foreach (var folder in folderNames)
                {
                    zipOptionFolders.Add(folder);
                }

                zipArchivePath = dialog.FileName;
                ZipOptionFileTextBlock.Text = Path.GetFileName(dialog.FileName);

                if (zipOptionFolders.Count == 0)
                {
                    SetZipOptionStatus("No folders were detected in this archive.", Brushes.OrangeRed);
                }
                else
                {
                    SetZipOptionStatus($"Loaded {zipOptionFolders.Count} folder option(s).", Brushes.LawnGreen);
                }
            }
            catch (Exception ex)
            {
                zipArchivePath = string.Empty;
                zipOptionFolders.Clear();
                ZipOptionFileTextBlock.Text = "No file loaded";
                SetZipOptionStatus($"Failed to read archive: {ex.Message}", Brushes.OrangeRed);
            }
        }

        private void ClearZipOptions(object sender, RoutedEventArgs e)
        {
            zipOptionFolders.Clear();
            ZipOptionFileTextBlock.Text = "No file loaded";
            ZipOptionStatusBorder.Visibility = Visibility.Collapsed;
            zipArchivePath = string.Empty;
        }

        private static IEnumerable<ZipFolderOption> CollectTopLevelFolders(ZipArchive archive)
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in archive.Entries)
            {
                var normalized = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length > 1)
                {
                    folders.Add(segments[0]);
                }
                else if (segments.Length == 1 && entry.FullName.EndsWith('/'))
                {
                    folders.Add(segments[0]);
                }
            }

            return folders
                .OrderBy(static f => f, StringComparer.OrdinalIgnoreCase)
                .Select(static name => new ZipFolderOption(name));
        }

        private void CopyZipOptionName(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not ZipFolderOption option)
            {
                return;
            }

            try
            {
                Clipboard.SetText(option.Name);
                SetZipOptionStatus($"Copied '{option.Name}' to clipboard.", Brushes.LawnGreen);
            }
            catch (Exception ex)
            {
                SetZipOptionStatus($"Unable to copy: {ex.Message}", Brushes.OrangeRed);
            }
        }

        private async void LaunchZipOption(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not ZipFolderOption option)
            {
                return;
            }

            await LaunchZipOptionAsync(option);
        }

        private async Task LaunchZipOptionAsync(ZipFolderOption option)
        {
            if (string.IsNullOrWhiteSpace(zipArchivePath) || !File.Exists(zipArchivePath))
            {
                SetZipOptionStatus("Load a ZIP archive before launching a profile.", Brushes.OrangeRed);
                return;
            }

            if (isLaunchingZip)
            {
                SetZipOptionStatus("Another launch is already in progress.", Brushes.OrangeRed);
                return;
            }

            var settingsService = ServiceLocator.GetService<RuriLibSettingsService>();
            var playwrightSettings = settingsService.RuriLibSettings?.PlaywrightSettings ?? new PlaywrightSettings();
            var firefoxBinary = playwrightSettings.FirefoxBinaryLocation;

            if (string.IsNullOrWhiteSpace(firefoxBinary) || !File.Exists(firefoxBinary))
            {
                SetZipOptionStatus("Firefox binary path in RL settings is invalid.", Brushes.OrangeRed);
                return;
            }

            var profileRoot = Path.Combine(Path.GetTempPath(), "ob2-zip-profile", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profileRoot);

            try
            {
                isLaunchingZip = true;
                SetZipOptionStatus($"Preparing profile '{option.Name}'...", Brushes.LightSteelBlue);

                await Task.Run(() => ExtractZipFolder(zipArchivePath, option.Name, profileRoot));

                if (!Directory.EnumerateFileSystemEntries(profileRoot).Any())
                {
                    throw new InvalidOperationException("The selected folder was not found in the archive.");
                }

                SetZipOptionStatus($"Launching Firefox for '{option.Name}'...", Brushes.LightSteelBlue);

                var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

                var launchOptions = new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = playwrightSettings.Headless,
                    ExecutablePath = firefoxBinary,
                    Timeout = playwrightSettings.TimeoutMilliseconds <= 0 ? 30000 : playwrightSettings.TimeoutMilliseconds,
                    Args = playwrightSettings.ExtraArgs ?? Array.Empty<string>()
                };

                var context = await playwright.Firefox.LaunchPersistentContextAsync(profileRoot, launchOptions);

                // Navigate to Gmail after launching browser
                try
                {
                    var pages = context.Pages;
                    var page = pages.Count > 0 ? pages[0] : await context.NewPageAsync();
                    await page.GotoAsync("https://gmail.com");
                    SetZipOptionStatus($"Launched Firefox profile '{option.Name}' and navigated to Gmail.", Brushes.LawnGreen);
                }
                catch (Exception navEx)
                {
                    SetZipOptionStatus($"Launched Firefox profile '{option.Name}' but failed to navigate to Gmail: {navEx.Message}", Brushes.Khaki);
                }

                var profile = new LaunchedZipProfile(playwright, context, profileRoot, option.Name);
                RegisterZipProfile(profile);

                var cookiesPath = Path.Combine(profileRoot, "cookies.sqlite");
                if (!File.Exists(cookiesPath))
                {
                    SetZipOptionStatus($"Launched '{option.Name}' but cookies.sqlite was not found.", Brushes.Khaki);
                }
                else
                {
                    if (!ZipOptionStatusBorder.Visibility.ToString().Contains("failed"))
                    {
                        SetZipOptionStatus($"Launched Firefox profile '{option.Name}' and navigated to Gmail.", Brushes.LawnGreen);
                    }
                }
            }
            catch (Exception ex)
            {
                TryDeleteDirectory(profileRoot);
                SetZipOptionStatus($"Launch failed: {ex.Message}", Brushes.OrangeRed);
            }
            finally
            {
                isLaunchingZip = false;
            }
        }

        private static void ExtractZipFolder(string archivePath, string folderName, string destination)
        {
            using var stream = File.OpenRead(archivePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            var prefix = folderName.TrimEnd('/') + "/";
            Directory.CreateDirectory(destination);

            foreach (var entry in archive.Entries)
            {
                var normalized = entry.FullName.Replace('\\', '/');
                if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = normalized[prefix.Length..];
                if (string.IsNullOrEmpty(relative))
                {
                    continue;
                }

                var targetPath = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));

                if (normalized.EndsWith("/", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                var targetDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                entry.ExtractToFile(targetPath, overwrite: true);
            }
        }

        private void SetZipOptionStatus(string message, Brush brush)
        {
            ZipOptionStatusTextBlock.Text = message;
            ZipOptionStatusTextBlock.Foreground = brush;
            ZipOptionStatusBorder.Visibility = Visibility.Visible;
        }

        private sealed class ZipFolderOption
        {
            public ZipFolderOption(string name)
            {
                Name = name;
            }

            public string Name { get; }
            public override string ToString() => Name;
        }

        private sealed class LaunchedZipProfile
        {
            public LaunchedZipProfile(IPlaywright playwright, IBrowserContext context, string profilePath, string optionName)
            {
                Playwright = playwright;
                Context = context;
                ProfilePath = profilePath;
                OptionName = optionName;
            }

            public IPlaywright Playwright { get; }
            public IBrowserContext Context { get; }
            public string ProfilePath { get; }
            public string OptionName { get; }
        }

        private static string? ExtractDetailedPatternLine(string line)
        {
            if (!line.Contains('♦') || !line.Contains('='))
            {
                return null;
            }

            var pattern = new Regex(@"^(.*?)♦(\d+)\s*\*(\d+)\s*♠(\S*)\s*@([^\s=]+)\s*=(\S+)(?:\s+(.*))?$");
            var match = pattern.Match(line);
            if (!match.Success)
            {
                return null;
            }

            var rawUsername = match.Groups[1].Value.Trim();
            var handle = match.Groups[5].Value.Trim('@');
            var sessionId = match.Groups[6].Value;
            var remainder = match.Groups[7].Value;

            var password = rawUsername.Length >= 3 ? rawUsername[^3..] + "@asem777" : "asem777";
            string? twoFaSecret = null;

            if (!string.IsNullOrWhiteSpace(remainder))
            {
                var twoFaMatch = Regex.Match(remainder, "2FA:(.*)", RegexOptions.IgnoreCase);
                if (twoFaMatch.Success)
                {
                    var before = remainder[..twoFaMatch.Index].Trim();
                    if (!string.IsNullOrEmpty(before))
                    {
                        password = before;
                    }
                    twoFaSecret = twoFaMatch.Groups[1].Value.Trim();
                }
                else
                {
                    password = remainder.Trim();
                }
            }

            var email = $"{rawUsername}@akunlama.com";

            var builder = new StringBuilder();
            builder.AppendLine(email);
            builder.AppendLine($"username: {handle}");
            builder.AppendLine($"password:{password}");
            builder.AppendLine($"sessionid={sessionId}");

            if (!string.IsNullOrEmpty(twoFaSecret))
            {
                builder.AppendLine($"Link Autentikasi: 2fa.akunlama.com/?secret={twoFaSecret}");
            }

            return builder.ToString();
        }

        private static string? ExtractFallbackLine(string line)
        {
            var emailMatch = Regex.Match(line, "\\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}\\b", RegexOptions.IgnoreCase);
            if (!emailMatch.Success)
            {
                return null;
            }

            var email = emailMatch.Value;
            var usernamePart = email.Split('@')[0];
            var suffix = usernamePart.Length >= 3 ? usernamePart[^3..] : null;
            var passwordLine = suffix != null ? $"charming@{suffix}" : "N/A";

            var authToken = Regex.Match(line, "auth_token=(\\w+)").Groups[1].Value;
            var sessionId = Regex.Match(line, "sessionid=(\\S+)").Groups[1].Value;
            var username = Regex.Match(line, "@(\\S+)").Groups[1].Value;
            var post = Regex.Match(line, "^(\\d+)♠").Groups[1].Value;
            var follower = Regex.Match(line, "(\\d+)~").Groups[1].Value;
            var year = Regex.Match(line, "~\\s*(\\d+)").Groups[1].Value;

            var builder = new StringBuilder();
            builder.AppendLine(email);
            builder.AppendLine($"username: {(string.IsNullOrEmpty(username) ? "N/A" : username)}");
            builder.AppendLine($"password: {passwordLine}");

            if (!string.IsNullOrEmpty(sessionId))
            {
                builder.AppendLine($"sessionid={sessionId}");
            }

            if (!string.IsNullOrEmpty(authToken))
            {
                builder.AppendLine($"auth_token={authToken}");
            }

            if (!string.IsNullOrEmpty(post) || !string.IsNullOrEmpty(follower) || !string.IsNullOrEmpty(year))
            {
                builder.AppendLine();
                builder.Append($"Username•Post•Follower•Tahun = {(string.IsNullOrEmpty(username) ? "N/A" : username)}•{(string.IsNullOrEmpty(post) ? "N/A" : post)}•{(string.IsNullOrEmpty(follower) ? "N/A" : follower)}•{(string.IsNullOrEmpty(year) ? "N/A" : year)}");
            }

            return builder.ToString();
        }

        private async void RefreshModemIp(object sender, RoutedEventArgs e)
        {
            var addressText = ModemAddressTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(addressText))
            {
                SetModemStatus("Router address is required.", Brushes.OrangeRed);
                return;
            }

            if (!addressText.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                addressText = $"http://{addressText}";
            }

            if (!Uri.TryCreate(addressText, UriKind.Absolute, out var baseUri))
            {
                SetModemStatus("Router address is invalid.", Brushes.OrangeRed);
                return;
            }

            var username = string.IsNullOrWhiteSpace(ModemUsernameTextBox.Text)
                ? "admin"
                : ModemUsernameTextBox.Text.Trim();
            var password = ModemPasswordBox.Password ?? string.Empty;

            RefreshModemIpButton.IsEnabled = false;
            SetModemStatus("Contacting modem…", Brushes.LightSteelBlue);
            AppendModemLog($"Target: {baseUri}");

            try
            {
                var cookieContainer = new CookieContainer();
                using var handler = new HttpClientHandler
                {
                    CookieContainer = cookieContainer,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };
                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(5)
                };

                ConfigureModemClient(client);

                var loginPayload = BuildLoginPayload(username, password);
                AppendModemLog("Sending login request.");
                var loginResponse = await SendModemRequest(client, baseUri, loginPayload);
                var loginBody = await loginResponse.Content.ReadAsStringAsync();
                AppendModemLog($"Login response {(int)loginResponse.StatusCode}: {Summarize(loginBody)}");

                loginResponse.EnsureSuccessStatusCode();

                var sessionCookie = FindSessionCookie(cookieContainer, baseUri);
                if (sessionCookie == null)
                {
                    throw new InvalidOperationException("Modem did not return a session cookie.");
                }

                var successCount = 0;
                foreach (var payload in ModemTogglePayloads.OrderBy(_ => modemRandom.Next()))
                {
                    var preference = ExtractPreferenceName(payload);
                    AppendModemLog($"Applying preference '{preference}'.");
                    var response = await SendModemRequest(client, baseUri, payload);
                    var responseBody = await response.Content.ReadAsStringAsync();
                    AppendModemLog($"Response {(int)response.StatusCode}: {Summarize(responseBody)}");

                    response.EnsureSuccessStatusCode();

                    if (responseBody.Contains("success", StringComparison.OrdinalIgnoreCase))
                    {
                        successCount++;
                    }
                }

                if (successCount > 0)
                {
                    SetModemStatus("Network toggles sent to modem.", Brushes.LawnGreen);
                }
                else
                {
                    SetModemStatus("Modem did not acknowledge the toggle requests.", Brushes.OrangeRed);
                }
            }
            catch (Exception ex)
            {
                AppendModemLog($"Error: {ex.Message}");
                SetModemStatus($"Failed: {ex.Message}", Brushes.OrangeRed);
            }
            finally
            {
                RefreshModemIpButton.IsEnabled = true;
            }
        }

        private void ClearModemLog(object sender, RoutedEventArgs e)
        {
            ModemLogTextBox.Clear();
            ModemStatusBorder.Visibility = Visibility.Collapsed;
        }

        private static void ConfigureModemClient(HttpClient client)
        {
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/javascript, */*; q=0.01");
            client.DefaultRequestHeaders.AcceptEncoding.Clear();
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");
            client.DefaultRequestHeaders.AcceptLanguage.Clear();
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            client.DefaultRequestHeaders.Add("DNT", "1");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.4896.60 Safari/537.36");
        }

        private static async Task<HttpResponseMessage> SendModemRequest(HttpClient client, Uri baseUri, string payload)
        {
            var endpoint = new Uri(baseUri, "/goform/goform_set_cmd_process");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded")
            };

            request.Headers.TryAddWithoutValidation("Origin", baseUri.GetLeftPart(UriPartial.Authority));
            request.Headers.Referrer = new Uri(baseUri, "/");

            return await client.SendAsync(request).ConfigureAwait(false);
        }

        private static System.Net.Cookie? FindSessionCookie(CookieContainer container, Uri baseUri)
        {
            foreach (System.Net.Cookie cookie in container.GetCookies(baseUri))
            {
                if (string.Equals(cookie.Name, "JSESSIONID", StringComparison.OrdinalIgnoreCase))
                {
                    return cookie;
                }
            }

            return null;
        }

        private static string BuildLoginPayload(string username, string password)
        {
            var credential = $"{username}\n{password}";
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential));
            return $"isTest=false&goformId=LOGIN&password={Uri.EscapeDataString(base64)}";
        }

        private static string ExtractPreferenceName(string payload)
        {
            const string key = "BearerPreference=";
            var start = payload.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return payload;
            }

            var value = payload[(start + key.Length)..];
            var end = value.IndexOf('%');
            if (end >= 0)
            {
                value = value[..end];
            }

            return value.Replace('_', ' ');
        }

        private static string Summarize(string input)
        {
            var text = input.Trim();
            if (text.Length == 0)
            {
                return "(empty)";
            }

            return text.Length > 120 ? text[..120] + "…" : text;
        }

        private void AppendModemLog(string message)
        {
            if (ModemLogTextBox == null)
            {
                return;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            ModemLogTextBox.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
            ModemLogTextBox.ScrollToEnd();
        }

        private void SetModemStatus(string message, Brush brush)
        {
            ModemStatusTextBlock.Text = message;
            ModemStatusTextBlock.Foreground = brush;
            ModemStatusBorder.Visibility = Visibility.Visible;
        }
    }
}
