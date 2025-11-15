using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Playwright;
using Microsoft.VisualBasic.Devices;
using Microsoft.Win32;
using OpenBullet2.Native.Infrastructure.DependencyInjection;
using OpenBullet2.Core.Services;
using RuriLib;
using RuriLib.Blocks.Utility;
using RuriLib.Helpers;
using RuriLib.Helpers.Transpilers;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Custom;
using RuriLib.Models.Configs;
using RuriLib.Models.Data;
using RuriLib.Models.Data.DataPools;
using RuriLib.Models.Bots;
using RuriLib.Logging;
using RuriLib.Models.Environment;
using RuriLib.Models.Settings;
using RuriLib.Services;

namespace OpenBullet2.Native.Views.Pages
{
    /// <summary>
    /// Utility tools dashboard.
    /// </summary>
    public partial class Tools : Page
    {
        private const int TotpPeriodSeconds = TwoFactorUtility.TotpPeriodSeconds;
        private static readonly string[] ModemTogglePayloads =
        [
            "isTest=false&goformId=SET_BEARER_PREFERENCE&BearerPreference=Only_LTE%0ALTE_preferred",
            "isTest=false&goformId=SET_BEARER_PREFERENCE&BearerPreference=NETWORK_auto%0ALTE_preferred",
            "isTest=false&goformId=SET_BEARER_PREFERENCE&BearerPreference=Only_LTE%0ALTE_preferred",
            "isTest=false&goformId=SET_BEARER_PREFERENCE&BearerPreference=NETWORK_auto%0ALTE_preferred"
        ];
        private const double CardMinWidth = 300;
        private const double CardMaxWidth = 420;
        private const double CardHorizontalSpacing = 16;
        private const int CardMaxColumns = 3;
        private const string AllCategoriesLabel = "All categories";

        private readonly DispatcherTimer timer;
        private string normalizedSecret = string.Empty;
        private string currentOtp = string.Empty;
        private readonly Random modemRandom = new();
        private readonly ObservableCollection<ZipFolderOption> zipOptionFolders = new();
        private readonly ObservableCollection<LineReducerCompareFile> lineReducerCompareFiles = new();
        private readonly List<LaunchedZipProfile> launchedZipProfiles = new();
        private readonly object zipProfileLock = new();
        private readonly List<ToolCardMetadata> toolCardCatalog = new();
        private string zipArchivePath = string.Empty;
        private bool isLaunchingZip;
        private bool isInitializingFilters;
        private CancellationTokenSource? lineReducerCts;
        private bool isLineReducerRunning;
        
        // Performance benchmark fields
        private readonly ComputerInfo computerInfo = new();
        private static readonly UTF8Encoding Utf8NoBomEncoding = new(false);

        public Tools()
        {
            InitializeComponent();

            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (_, _) => UpdateOtp();
            Unloaded += Tools_Unloaded;

            // Performance monitoring will be lazily initialized only when needed
            // This prevents expensive operations during page load and reduces navigation lag

            SetOtpDisplay("------", "Enter a secret key to generate codes.", 0);
            CopyOtpButton.IsEnabled = false;
            BookmarkletStatusBorder.Visibility = Visibility.Collapsed;
            TextCleanerStatusBorder.Visibility = Visibility.Collapsed;
            ZipOptionListBox.ItemsSource = zipOptionFolders;
            LineReducerCompareFilesListBox.ItemsSource = lineReducerCompareFiles;
            lineReducerCompareFiles.CollectionChanged += (_, _) => UpdateLineReducerCompareSummary();
            UpdateLineReducerCompareSummary();

            // Set initial values for performance display (will be updated lazily)
            InitializePerformanceDisplay();
            InitializeToolCardCatalogue();
        }

        private async void Tools_Unloaded(object sender, RoutedEventArgs e)
        {
            timer.Stop();
            await CleanupZipProfilesAsync();
        }

        private void SecretKeyTextChanged(object sender, TextChangedEventArgs e)
        {
            normalizedSecret = TwoFactorUtility.NormalizeSecret(SecretKeyTextBox.Text);
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

            if (TwoFactorUtility.TryGenerateOtp(normalizedSecret, DateTime.UtcNow, out var otp, out var secondsRemaining, out var error))
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

            if (TwoFactorUtility.TryGenerateOtp(normalizedSecret, DateTime.UtcNow, out var otp, out var secondsRemaining, out var error))
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

        private void InitializeToolCardCatalogue()
        {
            isInitializingFilters = true;

            toolCardCatalog.Clear();
            toolCardCatalog.Add(new ToolCardMetadata(ModemToolCard, "Modem IP Refresher", "Networking",
                "modem", "router", "wan", "gateway", "ip", "lease", "refresh"));
            toolCardCatalog.Add(new ToolCardMetadata(OtpToolCard, "OTP Toolkit", "Security",
                "two factor", "authenticator", "totp", "code", "2fa", "token"));
            toolCardCatalog.Add(new ToolCardMetadata(BookmarkletToolCard, "Bookmarklet Parser", "Automation",
                "javascript", "bookmark", "parser", "payload", "scrubber", "deobfuscate"));
            toolCardCatalog.Add(new ToolCardMetadata(TextCleanerToolCard, "Text Cleaner", "Text",
                "normalize", "whitespace", "dedupe", "cleanup", "formatter", "text", "sort"));
            toolCardCatalog.Add(new ToolCardMetadata(LineReducerToolCard, "Line Reducer", "Text",
                "compare", "dedupe", "difference", "filter", "txt", "large files"));
            toolCardCatalog.Add(new ToolCardMetadata(FirefoxToolCard, "Firefox Switcher", "Browsers",
                "profile", "browser", "automation", "firefox", "zip", "launcher", "profile manager"));
            toolCardCatalog.Add(new ToolCardMetadata(BenchmarkToolCard, "Performance Benchmark", "Performance",
                "metrics", "cpu", "memory", "system", "monitoring", "speed"));

            var categories = toolCardCatalog
                .Select(card => card.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category)
                .ToList();
            categories.Insert(0, AllCategoriesLabel);

            ToolCategoryComboBox.ItemsSource = categories;
            ToolCategoryComboBox.SelectedIndex = 0;
            ToolSearchTextBox.Text = string.Empty;
            ResetToolFiltersButton.IsEnabled = false;

            isInitializingFilters = false;
            ApplyToolFilters();
        }

        private void ToolSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (isInitializingFilters)
            {
                return;
            }

            ApplyToolFilters();
        }

        private void ToolCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isInitializingFilters)
            {
                return;
            }

            ApplyToolFilters();
        }

        private void ResetToolFilters(object sender, RoutedEventArgs e)
        {
            if (ToolSearchTextBox is null || ToolCategoryComboBox is null)
            {
                return;
            }

            var selectedCategory = ToolCategoryComboBox.SelectedItem as string;
            var hasCategoryFilter = !string.IsNullOrEmpty(selectedCategory) &&
                                    !string.Equals(selectedCategory, AllCategoriesLabel, StringComparison.OrdinalIgnoreCase);
            var hasSearch = !string.IsNullOrWhiteSpace(ToolSearchTextBox.Text);

            if (!hasCategoryFilter && !hasSearch)
            {
                return;
            }

            isInitializingFilters = true;
            ToolSearchTextBox.Text = string.Empty;
            ToolCategoryComboBox.SelectedIndex = 0;
            isInitializingFilters = false;

            ApplyToolFilters();
        }

        private void NavigateToToolCard(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            var alias = button.Tag as string ?? button.Content as string ?? string.Empty;
            var targetCard = toolCardCatalog.FirstOrDefault(card => card.HasAlias(alias));

            if (targetCard is null)
            {
                return;
            }

            if (targetCard.Card.Visibility != Visibility.Visible)
            {
                isInitializingFilters = true;
                ToolSearchTextBox.Text = string.Empty;
                ToolCategoryComboBox.SelectedIndex = 0;
                isInitializingFilters = false;
                ApplyToolFilters();
            }

            Dispatcher.InvokeAsync(() =>
            {
                targetCard.Card.BringIntoView();
                targetCard.Card.Focus();
            }, DispatcherPriority.Background);
        }

        private void ApplyToolFilters()
        {
            if (toolCardCatalog.Count == 0 || ToolCategoryComboBox is null || ToolSearchTextBox is null)
            {
                return;
            }

            var searchTerms = string.IsNullOrWhiteSpace(ToolSearchTextBox.Text)
                ? Array.Empty<string>()
                : ToolSearchTextBox.Text
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var selectedCategory = ToolCategoryComboBox.SelectedItem as string;
            var filterByCategory = !string.IsNullOrEmpty(selectedCategory) &&
                                   !string.Equals(selectedCategory, AllCategoriesLabel, StringComparison.OrdinalIgnoreCase);

            var visibleCount = 0;

            foreach (var metadata in toolCardCatalog)
            {
                var matchesCategory = !filterByCategory || metadata.IsInCategory(selectedCategory!);
                var matchesSearch = searchTerms.Length == 0 || metadata.MatchesSearchTerms(searchTerms);

                metadata.Card.Visibility = matchesCategory && matchesSearch
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                if (metadata.Card.Visibility == Visibility.Visible)
                {
                    visibleCount++;
                }
            }

            var filtersActive = filterByCategory || searchTerms.Length > 0;

            UpdateToolFilterStatus(visibleCount, toolCardCatalog.Count, filtersActive);
            NoToolMatchesTextBlock.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
            ResetToolFiltersButton.IsEnabled = filtersActive;
        }

        private void UpdateToolFilterStatus(int visibleCount, int totalCount, bool filtersActive)
        {
            if (!filtersActive)
            {
                ToolFilterStatusTextBlock.Visibility = Visibility.Collapsed;
                ToolFilterStatusTextBlock.Text = string.Empty;
                return;
            }

            ToolFilterStatusTextBlock.Text = visibleCount switch
            {
                0 => "No tools matched your filters.",
                _ when visibleCount == totalCount => $"All {totalCount} tools are visible.",
                _ => $"Showing {visibleCount} of {totalCount} tools."
            };

            ToolFilterStatusTextBlock.Visibility = Visibility.Visible;
        }

        private void SetOtpDisplay(string otp, string statusMessage, int elapsedSeconds)
        {
            currentOtp = otp;
            OtpTextBlock.Text = otp;
            OtpStatusTextBlock.Text = statusMessage;
            OtpProgressBar.Value = Math.Max(0, Math.Min(TotpPeriodSeconds, elapsedSeconds));
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
                normalized = normalized.Replace("\u2666\uFE0F", "\u2666");
                normalized = normalized.Replace("\u2660\uFE0F", "\u2660");
                normalized = normalized.Replace("\u2660 ", "\u2660");

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
            var match = Regex.Match(line, "\u2666(\\d+)");
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

        #region Line reducer

        private void BrowseLineReducerSource(object sender, RoutedEventArgs e)
        {
            if (isLineReducerRunning)
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                Title = "Select main text file"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            LineReducerSourcePathTextBox.Text = dialog.FileName;

            if (string.IsNullOrWhiteSpace(LineReducerOutputPathTextBox.Text))
            {
                LineReducerOutputPathTextBox.Text = SuggestLineReducerOutputPath(dialog.FileName);
            }
        }

        private void BrowseLineReducerOutput(object sender, RoutedEventArgs e)
        {
            if (isLineReducerRunning)
            {
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Title = "Choose output file",
                FileName = !string.IsNullOrWhiteSpace(LineReducerOutputPathTextBox.Text)
                    ? LineReducerOutputPathTextBox.Text
                    : SuggestLineReducerOutputPath(LineReducerSourcePathTextBox.Text)
            };

            if (dialog.ShowDialog() == true)
            {
                LineReducerOutputPathTextBox.Text = dialog.FileName;
            }
        }

        private void AddLineReducerCompareFiles(object sender, RoutedEventArgs e)
        {
            if (isLineReducerRunning)
            {
                SetLineReducerStatus("Wait for the current run to finish before editing files.", Brushes.OrangeRed);
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = true,
                Title = "Add comparison files"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var added = 0;
            var skipped = 0;

            foreach (var fileName in dialog.FileNames)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                string normalizedPath;
                try
                {
                    normalizedPath = Path.GetFullPath(fileName);
                }
                catch
                {
                    skipped++;
                    continue;
                }

                if (string.Equals(normalizedPath, LineReducerSourcePathTextBox.Text, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                if (lineReducerCompareFiles.Any(existing =>
                        existing.FullPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var info = new FileInfo(normalizedPath);
                    if (!info.Exists)
                    {
                        skipped++;
                        continue;
                    }

                    lineReducerCompareFiles.Add(new LineReducerCompareFile(info.FullName, info.Length));
                    added++;
                }
                catch
                {
                    skipped++;
                }
            }

            UpdateLineReducerCompareSummary();

            if (added > 0)
            {
                SetLineReducerStatus($"Added {added} comparison file(s).", Brushes.LawnGreen);
            }
            else if (skipped > 0)
            {
                SetLineReducerStatus("No new comparison files were added.", Brushes.OrangeRed);
            }
        }

        private void ClearLineReducerCompareFiles(object sender, RoutedEventArgs e)
        {
            if (isLineReducerRunning || lineReducerCompareFiles.Count == 0)
            {
                return;
            }

            lineReducerCompareFiles.Clear();
            UpdateLineReducerCompareSummary();
            SetLineReducerStatus("Cleared comparison list.", Brushes.OrangeRed);
        }

        private void RemoveLineReducerCompareFile(object sender, RoutedEventArgs e)
        {
            if (isLineReducerRunning || sender is not Button { Tag: LineReducerCompareFile entry })
            {
                return;
            }

            lineReducerCompareFiles.Remove(entry);
            UpdateLineReducerCompareSummary();
        }

        private void UpdateLineReducerCompareSummary()
        {
            if (LineReducerCompareSummaryTextBlock is null)
            {
                return;
            }

            if (lineReducerCompareFiles.Count == 0)
            {
                LineReducerCompareSummaryTextBlock.Text = "No comparison files selected.";
                return;
            }

            var totalBytes = lineReducerCompareFiles.Sum(file => file.Length);
            LineReducerCompareSummaryTextBlock.Text =
                $"{lineReducerCompareFiles.Count} file(s) • {FormatBytes(totalBytes)} total";
        }

        private async void RunLineReducer(object sender, RoutedEventArgs e)
        {
            if (isLineReducerRunning)
            {
                return;
            }

            var sourcePath = (LineReducerSourcePathTextBox.Text ?? string.Empty).Trim();
            var outputPath = (LineReducerOutputPathTextBox.Text ?? string.Empty).Trim();
            var comparisonFiles = lineReducerCompareFiles.Select(file => file.FullPath).ToList();

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                SetLineReducerStatus("Select an existing main file to continue.", Brushes.OrangeRed);
                return;
            }

            if (comparisonFiles.Count == 0)
            {
                SetLineReducerStatus("Add at least one comparison file.", Brushes.OrangeRed);
                return;
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = SuggestLineReducerOutputPath(sourcePath);
                LineReducerOutputPathTextBox.Text = outputPath;
            }

            string normalizedOutput;
            string normalizedSource;

            try
            {
                normalizedOutput = Path.GetFullPath(outputPath);
                normalizedSource = Path.GetFullPath(sourcePath);
            }
            catch (Exception ex)
            {
                SetLineReducerStatus($"Invalid path: {ex.Message}", Brushes.OrangeRed);
                return;
            }

            if (string.Equals(normalizedOutput, normalizedSource, StringComparison.OrdinalIgnoreCase))
            {
                SetLineReducerStatus("Output file must be different from the main file.", Brushes.OrangeRed);
                return;
            }

            if (comparisonFiles.Any(file =>
                    string.Equals(Path.GetFullPath(file), normalizedOutput, StringComparison.OrdinalIgnoreCase)))
            {
                SetLineReducerStatus("Output file cannot overwrite a comparison file.", Brushes.OrangeRed);
                return;
            }

            try
            {
                var outputDirectory = Path.GetDirectoryName(normalizedOutput);
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }
            }
            catch (Exception ex)
            {
                SetLineReducerStatus($"Unable to create output directory: {ex.Message}", Brushes.OrangeRed);
                return;
            }

            SetLineReducerBusyState(true);
            SetLineReducerStatus("Indexing comparison files...", Brushes.LightSteelBlue);
            LineReducerProgressBar.Value = 0;
            LineReducerProgressBar.Visibility = Visibility.Visible;
            LineReducerProgressTextBlock.Text = "Preparing...";

            lineReducerCts?.Dispose();
            lineReducerCts = new CancellationTokenSource();

            var options = new LineReducerOptions(
                TrimWhitespace: LineReducerTrimCheckBox.IsChecked == true,
                IgnoreCase: LineReducerIgnoreCaseCheckBox.IsChecked == true);

            try
            {
                var progress = new Progress<LineReductionProgress>(UpdateLineReducerProgress);
                var result = await ExecuteLineReducerAsync(sourcePath, comparisonFiles, normalizedOutput, options, progress, lineReducerCts.Token);

                SetLineReducerStatus($"Completed. Removed {result.RemovedLines:N0} line(s).", Brushes.LawnGreen);
                LineReducerStatsTextBlock.Text =
                    $"Indexed {result.IndexedLines:N0} comparison lines ({FormatBytes(result.ComparisonBytes)})." +
                    $"{Environment.NewLine}Processed {result.ProcessedSourceLines:N0} source lines " +
                    $"({FormatBytes(result.SourceBytes)}): kept {result.WrittenLines:N0}, removed {result.RemovedLines:N0}." +
                    $"{Environment.NewLine}Elapsed {result.Elapsed:mm\\:ss}. Output saved to {normalizedOutput}.";
            }
            catch (OperationCanceledException)
            {
                SetLineReducerStatus("Operation cancelled.", Brushes.OrangeRed);
                TryDeleteFile(normalizedOutput);
            }
            catch (Exception ex)
            {
                SetLineReducerStatus($"Line reduction failed: {ex.Message}", Brushes.OrangeRed);
                TryDeleteFile(normalizedOutput);
            }
            finally
            {
                LineReducerProgressBar.Visibility = Visibility.Collapsed;
                LineReducerProgressTextBlock.Text = "Idle.";
                lineReducerCts?.Dispose();
                lineReducerCts = null;
                SetLineReducerBusyState(false);
            }
        }

        private void CancelLineReducer(object sender, RoutedEventArgs e)
        {
            if (!isLineReducerRunning)
            {
                return;
            }

            lineReducerCts?.Cancel();
        }

        private void SetLineReducerBusyState(bool isBusy)
        {
            isLineReducerRunning = isBusy;

            var isEnabled = !isBusy;

            LineReducerSourcePathTextBox.IsEnabled = isEnabled;
            LineReducerOutputPathTextBox.IsEnabled = isEnabled;
            LineReducerTrimCheckBox.IsEnabled = isEnabled;
            LineReducerIgnoreCaseCheckBox.IsEnabled = isEnabled;
            BrowseLineReducerSourceButton.IsEnabled = isEnabled;
            BrowseLineReducerOutputButton.IsEnabled = isEnabled;
            AddLineReducerCompareButton.IsEnabled = isEnabled;
            ClearLineReducerCompareButton.IsEnabled = isEnabled;
            LineReducerCompareFilesListBox.IsEnabled = isEnabled;
            RunLineReducerButton.IsEnabled = isEnabled;
            CancelLineReducerButton.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetLineReducerStatus(string message, Brush brush)
        {
            LineReducerStatusTextBlock.Text = message;
            LineReducerStatusTextBlock.Foreground = brush;
            LineReducerStatusBorder.Visibility = Visibility.Visible;
        }

        private void UpdateLineReducerProgress(LineReductionProgress progress)
        {
            LineReducerProgressBar.Visibility = Visibility.Visible;
            LineReducerProgressBar.Value = Math.Max(0, Math.Min(100, progress.Percent));

            var builder = new StringBuilder(progress.Stage);
            builder.Append($" | Removed {progress.RemovedLines:N0} line(s)");
            if (progress.ProcessedSourceLines > 0)
            {
                builder.Append($", processed {progress.ProcessedSourceLines:N0} line(s)");
            }

            LineReducerProgressTextBlock.Text = builder.ToString();
        }

        private static string SuggestLineReducerOutputPath(string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "reduced.txt");
            }

            var directory = Path.GetDirectoryName(sourcePath);
            var fileName = Path.GetFileNameWithoutExtension(sourcePath);
            var extension = Path.GetExtension(sourcePath);

            var candidateName = $"{fileName}_reduced{extension}";
            return string.IsNullOrEmpty(directory)
                ? candidateName
                : Path.Combine(directory, candidateName);
        }

        private static async Task<LineReducerResult> ExecuteLineReducerAsync(
            string sourcePath,
            IReadOnlyList<string> comparisonFiles,
            string outputPath,
            LineReducerOptions options,
            IProgress<LineReductionProgress>? progress,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            var comparisonBytes = comparisonFiles.Sum(GetFileLengthSafe);
            var sourceBytes = GetFileLengthSafe(sourcePath);
            var totalBytes = Math.Max(1, comparisonBytes + sourceBytes);

            var signatures = new HashSet<LineFingerprint>();
            long indexedLines = 0;
            long comparisonBytesCompleted = 0;

            foreach (var comparison in comparisonFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await using var stream = new FileStream(comparison, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 1 << 20, useAsync: true);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1 << 20);

                while (true)
                {
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    var fingerprint = LineFingerprint.Create(line, options.TrimWhitespace, options.IgnoreCase);
                    signatures.Add(fingerprint);
                    indexedLines++;

                    if (indexedLines % 25000 == 0)
                    {
                        var percent = (comparisonBytesCompleted + stream.Position) / (double)totalBytes * 100d;
                        progress?.Report(new LineReductionProgress(
                            Percent: Math.Min(98, percent),
                            Stage: $"Indexing comparison files ({indexedLines:N0})",
                            ProcessedSourceLines: 0,
                            RemovedLines: 0,
                            WrittenLines: 0,
                            IndexedLines: indexedLines));
                    }
                }

                comparisonBytesCompleted += stream.Position;
                var percentAfterFile = comparisonBytesCompleted / (double)totalBytes * 100d;
                progress?.Report(new LineReductionProgress(
                    Percent: Math.Min(99, percentAfterFile),
                    Stage: $"Indexed {indexedLines:N0} comparison lines",
                    ProcessedSourceLines: 0,
                    RemovedLines: 0,
                    WrittenLines: 0,
                    IndexedLines: indexedLines));
            }

            var newline = DetectSourceNewLine(sourcePath);

            long processedLines = 0;
            long removedLines = 0;
            long writtenLines = 0;

            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 20, useAsync: true);
            using var sourceReader = new StreamReader(sourceStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
                bufferSize: 1 << 20);
            _ = sourceReader.Peek();
            var writerEncoding = DetermineOutputEncoding(sourceReader);

            await using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1 << 20, useAsync: true);
            using var writer = new StreamWriter(outputStream, writerEncoding, bufferSize: 1 << 20, leaveOpen: false);
            writer.NewLine = newline;

            while (true)
            {
                var line = await sourceReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();

                processedLines++;

                var fingerprint = LineFingerprint.Create(line, options.TrimWhitespace, options.IgnoreCase);
                if (signatures.Contains(fingerprint))
                {
                    removedLines++;
                }
                else
                {
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                    writtenLines++;
                }

                if (processedLines % 5000 == 0)
                {
                    var percent = (comparisonBytes + sourceStream.Position) / (double)totalBytes * 100d;
                    progress?.Report(new LineReductionProgress(
                        Percent: Math.Min(100, percent),
                        Stage: $"Processing source ({processedLines:N0})",
                        ProcessedSourceLines: processedLines,
                        RemovedLines: removedLines,
                        WrittenLines: writtenLines,
                        IndexedLines: indexedLines));
                }
            }

            await writer.FlushAsync().ConfigureAwait(false);
            stopwatch.Stop();

            progress?.Report(new LineReductionProgress(
                Percent: 100,
                Stage: "Completed",
                ProcessedSourceLines: processedLines,
                RemovedLines: removedLines,
                WrittenLines: writtenLines,
                IndexedLines: indexedLines));

            return new LineReducerResult(
                ProcessedSourceLines: processedLines,
                RemovedLines: removedLines,
                WrittenLines: writtenLines,
                IndexedLines: indexedLines,
                SourceBytes: sourceBytes,
                ComparisonBytes: comparisonBytes,
                Elapsed: stopwatch.Elapsed);
        }

        private static long GetFileLengthSafe(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? info.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        #endregion

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

            var post = Regex.Match(line, "^(\\d+)Ã¢â„¢Â ").Groups[1].Value;
            var follower = Regex.Match(line, "(\\d+)~").Groups[1].Value;
            var year = Regex.Match(line, "~\\s*(\\d+)").Groups[1].Value;

            var passwordLine = emailSuffix != null ? $"charming@{emailSuffix}" : "N/A";

            var builder = new StringBuilder();
            builder.AppendLine(email);
            builder.AppendLine($"password: {passwordLine}");
            builder.AppendLine($"check email: akunlama.com/inbox/{usernamePart}");
            builder.AppendLine($"auth_token={authToken ?? "N/A"}");
            builder.AppendLine();
            builder.Append($"UsernameÃ¢â‚¬Â¢PostÃ¢â‚¬Â¢FollowerÃ¢â‚¬Â¢Tahun = {username ?? "N/A"}Ã¢â‚¬Â¢{post}");
            builder.Append($"Ã¢â‚¬Â¢{(string.IsNullOrEmpty(follower) ? "N/A" : follower)}");
            builder.Append($"Ã¢â‚¬Â¢{(string.IsNullOrEmpty(year) ? "N/A" : year)}");
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

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // Ignore deletion failures
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            var sizes = new[] { "B", "KB", "MB", "GB", "TB" };
            var magnitude = (int)Math.Floor(Math.Log(bytes, 1024));
            magnitude = Math.Clamp(magnitude, 0, sizes.Length - 1);
            var adjusted = bytes / Math.Pow(1024, magnitude);
            return $"{adjusted:0.##} {sizes[magnitude]}";
        }

        private static string DetectSourceNewLine(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 4096, FileOptions.SequentialScan);

                var previous = -1;
                while (true)
                {
                    var current = stream.ReadByte();
                    if (current == -1)
                    {
                        return Environment.NewLine;
                    }

                    if (current == '\n')
                    {
                        return previous == '\r' ? "\r\n" : "\n";
                    }

                    if (previous == '\r')
                    {
                        return "\r";
                    }

                    previous = current;
                }
            }
            catch
            {
                return Environment.NewLine;
            }
        }

        private static Encoding DetermineOutputEncoding(StreamReader reader)
        {
            try
            {
                var encoding = reader.CurrentEncoding;
                if (encoding is UTF8Encoding)
                {
                    return Utf8NoBomEncoding;
                }

                return encoding ?? Utf8NoBomEncoding;
            }
            catch
            {
                return Utf8NoBomEncoding;
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
            const string SeparatorMarker = "\u00C3\u00A2\u00E2\u201E\u00A2\u00C2\u00A6";

            if (!line.Contains(SeparatorMarker, StringComparison.Ordinal) || !line.Contains('='))
            {
                return null;
            }

            var pattern = new Regex($@"^(.*?){SeparatorMarker}(\d+)\s*\*(\d+)\s*{SeparatorMarker}(\S*)\s*@([^\s=]+)\s*=(\S+)(?:\s+(.*))?$");
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
            var post = Regex.Match(line, "^(\\d+)Ã¢â„¢Â ").Groups[1].Value;
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
                builder.Append($"UsernameÃ¢â‚¬Â¢PostÃ¢â‚¬Â¢FollowerÃ¢â‚¬Â¢Tahun = {(string.IsNullOrEmpty(username) ? "N/A" : username)}Ã¢â‚¬Â¢{(string.IsNullOrEmpty(post) ? "N/A" : post)}Ã¢â‚¬Â¢{(string.IsNullOrEmpty(follower) ? "N/A" : follower)}Ã¢â‚¬Â¢{(string.IsNullOrEmpty(year) ? "N/A" : year)}");
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
            SetModemStatus("Contacting modemÃ¢â‚¬Â¦", Brushes.LightSteelBlue);
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

            return text.Length > 120 ? text[..120] + "Ã¢â‚¬Â¦" : text;
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

        #region Performance Benchmark
        
        private DispatcherTimer benchmarkUpdateTimer;
        private DateTime benchmarkStartTime;
        private Stopwatch benchmarkStopwatch;
        private bool benchmarkInitialized = false;
        private bool performanceMonitoringStarted = false;

        private void LazyInitializePerformanceMonitoring()
        {
            if (benchmarkInitialized) return;

            benchmarkInitialized = true;
            performanceMonitoringStarted = true;
            
            // Use a longer interval to reduce CPU load
            benchmarkUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5) // Increased from 2 to 5 seconds
            };
            benchmarkUpdateTimer.Tick += (_, _) => UpdatePerformanceStats();
            benchmarkUpdateTimer.Start();
        }

        private void UpdatePerformanceStats()
        {
            if (!performanceMonitoringStarted) return;

            try
            {
                // Lightweight performance updates - only update essential information
                var memoryInfo = GetLightweightMemoryUsage();
                if (memoryInfo.usedMB != null)
                {
                    MemoryUsageValue.Text = $"{memoryInfo.usedMB} MB / {memoryInfo.totalMB} MB";
                    MemoryUsageTextBlock.Text = memoryInfo.percentage.ToString("F1") + "%";
                    MemoryUsageTextBlock.Foreground = GetPerformanceColor(memoryInfo.percentage);

                    // Update CPU usage with lower frequency
                    var cpuUsage = GetCpuUsage();
                    CpuUsageValue.Text = cpuUsage.ToString("F1") + "%";
                    CpuUsageTextBlock.Text = cpuUsage > 80 ? "HIGH" : cpuUsage > 50 ? "MED" : "LOW";
                    CpuUsageTextBlock.Foreground = GetPerformanceColor(cpuUsage);
                }

                // Update system status (less frequently to reduce CPU load)
                var systemStatus = GetSystemStatus();
                if (!string.IsNullOrEmpty(systemStatus))
                {
                    SystemStatusValue.Text = systemStatus;
                    SystemStatusTextBlock.Text = systemStatus == "Optimal" ? "GOOD" : 
                                               systemStatus == "Moderate" ? "WARN" : "POOR";
                    SystemStatusTextBlock.Foreground = systemStatus == "Optimal" ? Brushes.LightGreen :
                                                     systemStatus == "Moderate" ? Brushes.Orange : Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                // Minimize logging for performance
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating performance stats: {ex.Message}");
                }
            }
        }

        private (string? usedMB, string totalMB, double percentage) GetLightweightMemoryUsage()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64;
                var totalMemory = computerInfo.TotalPhysicalMemory;
                var percentage = totalMemory > 0 ? (double)workingSet / totalMemory * 100.0 : 0;

                return (
                    usedMB: (workingSet / 1024 / 1024).ToString(),
                    totalMB: (totalMemory / 1024 / 1024).ToString(),
                    percentage
                );
            }
            catch
            {
                return (null, "N/A", 0);
            }
        }

        private void ClearBenchmarkStats(object sender, RoutedEventArgs e)
        {
            BenchmarkOutputTextBox.Clear();
            BenchmarkStatusBorder.Visibility = Visibility.Collapsed;
            AppendBenchmarkLog("Performance statistics cleared.");
        }

        private sealed class BenchmarkContext
        {
            public ConfigService ConfigService { get; set; }
            public RuriLibSettingsService SettingsService { get; set; }
            public PluginRepository PluginRepository { get; set; }
            public BotData BotData { get; set; }
        }

        private sealed class BenchmarkResult
        {
            public BenchmarkResult(string name, TimeSpan duration, string details, bool success, bool skipped, string errorMessage)
            {
                Name = name;
                Duration = duration;
                Details = details;
                Success = success;
                Skipped = skipped;
                ErrorMessage = errorMessage;
            }

            public string Name { get; }
            public TimeSpan Duration { get; }
            public string Details { get; }
            public bool Success { get; }
            public bool Skipped { get; }
            public string ErrorMessage { get; }

            public static BenchmarkResult SuccessResult(string name, TimeSpan duration, string details)
                => new(name, duration, details, true, false, null);

            public static BenchmarkResult Failure(string name, TimeSpan duration, string error)
                => new(name, duration, string.Empty, false, false, error);

            public static BenchmarkResult SkippedResult(string name, string reason)
                => new(name, TimeSpan.Zero, reason, false, true, null);
        }

        private async void RunPerformanceBenchmark(object sender, RoutedEventArgs e)
        {
            RunBenchmarkButton.IsEnabled = false;

            try
            {
                if (!performanceMonitoringStarted)
                {
                    LazyInitializePerformanceMonitoring();
                }

                benchmarkStartTime = DateTime.Now;
                benchmarkStopwatch = Stopwatch.StartNew();

                SetBenchmarkStatus("Running software performance benchmark...", Brushes.LightBlue);
                AppendBenchmarkLog($"Benchmark started at {benchmarkStartTime:HH:mm:ss}");

                var context = BuildBenchmarkContext();
                var steps = new List<Func<BenchmarkContext, Task<BenchmarkResult>>>
                {
                    BenchmarkConfigReloadAsync,
                    BenchmarkConfigSerializationAsync,
                    BenchmarkStringBlockAsync,
                    BenchmarkLoliCodeParsingAsync,
                    BenchmarkWordlistDataPoolAsync,
                    BenchmarkPluginDiscoveryAsync
                };

                var results = new List<BenchmarkResult>();

                foreach (var step in steps)
                {
                    var result = await step(context);
                    results.Add(result);
                    AppendBenchmarkResultLog(result);
                }

                benchmarkStopwatch.Stop();

                var passed = results.Count(r => r.Success);
                var skipped = results.Count(r => r.Skipped);
                var failed = results.Count - passed - skipped;
                var totalDuration = results.Aggregate(TimeSpan.Zero, (acc, current) => acc + current.Duration);

                AppendBenchmarkLog("=== BENCHMARK COMPLETE ===");
                AppendBenchmarkLog($"Tests passed: {passed}, failed: {failed}, skipped: {skipped}");
                AppendBenchmarkLog($"Aggregate runtime: {totalDuration.TotalMilliseconds:F0}ms (wall clock {benchmarkStopwatch.ElapsedMilliseconds}ms)");
                AppendBenchmarkLog($"Benchmark completed at {DateTime.Now:HH:mm:ss}");

                if (failed > 0)
                {
                    SetBenchmarkStatus("Software benchmark completed with errors", Brushes.OrangeRed);
                }
                else if (passed == 0)
                {
                    SetBenchmarkStatus("Software benchmark could not run", Brushes.Orange);
                }
                else if (skipped > 0)
                {
                    SetBenchmarkStatus("Software benchmark completed with partial coverage", Brushes.Gold);
                }
                else
                {
                    SetBenchmarkStatus("Software benchmark completed successfully", Brushes.LightGreen);
                }
            }
            catch (Exception ex)
            {
                benchmarkStopwatch?.Stop();
                AppendBenchmarkLog($"Benchmark aborted: {ex.Message}");
                SetBenchmarkStatus($"Benchmark failed: {ex.Message}", Brushes.Red);
            }
            finally
            {
                RunBenchmarkButton.IsEnabled = true;
            }
        }

        private BenchmarkContext BuildBenchmarkContext()
        {
            var context = new BenchmarkContext();

            try
            {
                context.ConfigService = ServiceLocator.GetOptionalService<ConfigService>();
            }
            catch (Exception ex)
            {
                AppendBenchmarkLog($"Config service unavailable: {ex.Message}");
            }

            try
            {
                context.SettingsService = ServiceLocator.GetOptionalService<RuriLibSettingsService>();
            }
            catch (Exception ex)
            {
                AppendBenchmarkLog($"Settings service unavailable: {ex.Message}");
            }

            if (context.SettingsService == null)
            {
                try
                {
                    context.SettingsService = new RuriLibSettingsService(GetBenchmarkSettingsPath());
                }
                catch (Exception ex)
                {
                    AppendBenchmarkLog($"Fallback settings initialization failed: {ex.Message}");
                }
            }

            try
            {
                context.PluginRepository = ServiceLocator.GetOptionalService<PluginRepository>();
            }
            catch (Exception ex)
            {
                AppendBenchmarkLog($"Plugin repository unavailable: {ex.Message}");
            }

            context.BotData = CreateBenchmarkBotData(context.SettingsService);

            return context;
        }

        private static string GetBenchmarkSettingsPath()
        {
            var path = Path.Combine(Path.GetTempPath(), "ob2-benchmark", "settings");
            Directory.CreateDirectory(path);
            return path;
        }

        private BotData CreateBenchmarkBotData(RuriLibSettingsService settingsService)
        {
            try
            {
                var effectiveSettings = settingsService ?? new RuriLibSettingsService(GetBenchmarkSettingsPath());
                var providers = new Providers(effectiveSettings);
                var logger = new BotLogger { Enabled = false };
                var wordlistType = effectiveSettings.Environment?.WordlistTypes?.FirstOrDefault()
                    ?? new WordlistType
                    {
                        Name = "Benchmark",
                        Regex = ".*",
                        Verify = false,
                        Separator = ":",
                        Slices = new[] { "DATA", "EXTRA" },
                        SlicesAlias = Array.Empty<string>()
                    };

                var dataLine = new DataLine("benchmark:data", wordlistType);
                return new BotData(providers, new RuriLib.Models.Configs.ConfigSettings(), logger, dataLine);
            }
            catch (Exception ex)
            {
                AppendBenchmarkLog($"Bot context initialization failed: {ex.Message}");
                return null;
            }
        }

        private void AppendBenchmarkResultLog(BenchmarkResult result)
        {
            if (result == null)
            {
                AppendBenchmarkLog("Benchmark step returned no result.");
                return;
            }

            if (result.Skipped)
            {
                AppendBenchmarkLog($"[Skipped] {result.Name}: {result.Details}");
            }
            else if (!result.Success)
            {
                var message = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Unknown error" : result.ErrorMessage;
                AppendBenchmarkLog($"[Failed] {result.Name} ({result.Duration.TotalMilliseconds:F0}ms): {message}");
            }
            else
            {
                AppendBenchmarkLog($"[Passed] {result.Name} ({result.Duration.TotalMilliseconds:F0}ms): {result.Details}");
            }
        }

        private async Task<BenchmarkResult> BenchmarkConfigReloadAsync(BenchmarkContext context)
        {
            const string name = "Config cache refresh";

            if (context.ConfigService == null)
            {
                return BenchmarkResult.SkippedResult(name, "Config service unavailable");
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                await context.ConfigService.ReloadConfigsAsync();
                var elapsed = stopwatch.Elapsed;
                var configCount = context.ConfigService.Configs?.Count() ?? 0;
                var details = $"{configCount} config(s) cached";
                return BenchmarkResult.SuccessResult(name, elapsed, details);
            }
            catch (Exception ex)
            {
                var elapsed = stopwatch.Elapsed;
                return BenchmarkResult.Failure(name, elapsed, ex.Message);
            }
        }

        private async Task<BenchmarkResult> BenchmarkConfigSerializationAsync(BenchmarkContext _)
        {
            const string name = "Config serialization";
            var config = BuildSampleBenchmarkConfig();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var packed = await ConfigPacker.PackAsync(config);
                using var stream = new MemoryStream(packed);
                var unpacked = await ConfigPacker.UnpackAsync(stream);
                var elapsed = stopwatch.Elapsed;
                var sizeKb = packed.Length / 1024d;
                var details = $"Packed {unpacked.Metadata?.Name ?? "config"} ({sizeKb:F1} KB)";
                return BenchmarkResult.SuccessResult(name, elapsed, details);
            }
            catch (Exception ex)
            {
                var elapsed = stopwatch.Elapsed;
                return BenchmarkResult.Failure(name, elapsed, ex.Message);
            }
        }

        private Task<BenchmarkResult> BenchmarkStringBlockAsync(BenchmarkContext context)
        {
            const string name = "String block throughput";
            var botData = context.BotData;

            if (botData == null)
            {
                return Task.FromResult(BenchmarkResult.SkippedResult(name, "Bot context unavailable"));
            }

            var samples = new[]
            {
                "The quick brown fox jumps over the lazy dog.",
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit.",
                "OpenBullet2 diagnostics benchmark string payload.",
                "RuriLib string functions under load."
            };

            var replacements = new[] { "a", "e", "i", "o", "u" };
            const int iterations = 100_000;

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var lengthAccumulator = 0;

                for (var i = 0; i < iterations; i++)
                {
                    var input = samples[i % samples.Length];

                    var upper = RuriLib.Blocks.Functions.String.Methods.ToUppercase(botData, input);
                    var reversed = RuriLib.Blocks.Functions.String.Methods.Reverse(botData, upper);

                    var sliceLength = Math.Min(16, reversed.Length);
                    var sliced = sliceLength > 0
                        ? RuriLib.Blocks.Functions.String.Methods.Substring(botData, reversed, 0, sliceLength)
                        : string.Empty;

                    var replaced = RuriLib.Blocks.Functions.String.Methods.Replace(
                        botData,
                        sliced,
                        replacements[i % replacements.Length],
                        replacements[(i + 1) % replacements.Length]);

                    var random = RuriLib.Blocks.Functions.String.Methods.RandomString(botData, "?l?u?d?l?u?d");

                    lengthAccumulator += (replaced?.Length ?? 0) + (random?.Length ?? 0);
                }

                stopwatch.Stop();

                var opsPerSecond = iterations / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
                var details = $"{iterations:N0} string block invocations (~{opsPerSecond:N0} ops/s, aggregate output {lengthAccumulator:N0} chars)";
                return Task.FromResult(BenchmarkResult.SuccessResult(name, stopwatch.Elapsed, details));
            }
            catch (Exception ex)
            {
                var elapsed = stopwatch.Elapsed;
                return Task.FromResult(BenchmarkResult.Failure(name, elapsed, ex.Message));
            }
        }

        private Config BuildSampleBenchmarkConfig()
        {
            return new Config
            {
                Id = $"benchmark-{Guid.NewGuid():N}",
                Mode = ConfigMode.LoliCode,
                Metadata = new RuriLib.Models.Configs.ConfigMetadata
                {
                    Name = "Benchmark Sample Config",
                    Category = "Diagnostics",
                    Author = "OpenBullet2"
                },
                Settings = new RuriLib.Models.Configs.ConfigSettings(),
                Readme = "Synthetic config generated for diagnostics.",
                LoliCodeScript = "LOG \"Benchmark\"",
                StartupLoliCodeScript = "LOG \"Benchmark startup\""
            };
        }

        private static string BuildBenchmarkLoliScript()
        {
            var ids = Globals.DescriptorsRepository.Descriptors
                .Where(pair => pair.Value is AutoBlockDescriptor autoDescriptor && autoDescriptor.Parameters.Count == 0)
                .Select(pair => pair.Key)
                .Distinct()
                .Take(12)
                .ToList();

            if (!ids.Any())
            {
                return string.Empty;
            }

            var builder = new StringBuilder();

            foreach (var id in ids)
            {
                builder.AppendLine($"BLOCK:{id}");
                builder.AppendLine("ENDBLOCK");
            }

            return builder.ToString();
        }

        private Task<BenchmarkResult> BenchmarkLoliCodeParsingAsync(BenchmarkContext _)
        {
            const string name = "LoliCode transpiler";
            var script = BuildBenchmarkLoliScript();

            if (string.IsNullOrWhiteSpace(script))
            {
                return Task.FromResult(BenchmarkResult.SkippedResult(name, "No parameterless blocks available for testing"));
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var stack = Loli2StackTranspiler.Transpile(script);
                var elapsed = stopwatch.Elapsed;
                var details = $"Transpiled {stack.Count} block(s)";
                return Task.FromResult(BenchmarkResult.SuccessResult(name, elapsed, details));
            }
            catch (Exception ex)
            {
                var elapsed = stopwatch.Elapsed;
                return Task.FromResult(BenchmarkResult.Failure(name, elapsed, ex.Message));
            }
        }

        private Task<BenchmarkResult> BenchmarkWordlistDataPoolAsync(BenchmarkContext context)
        {
            const string name = "Wordlist ingestion";

            var wordlistType = context.SettingsService?.Environment?.WordlistTypes?.FirstOrDefault();

            if (wordlistType == null)
            {
                return Task.FromResult(BenchmarkResult.SkippedResult(name, "No wordlist types configured"));
            }

            var tempFile = Path.Combine(Path.GetTempPath(), $"ob2-wordlist-{Guid.NewGuid():N}.txt");
            var entries = GenerateBenchmarkWordlistEntries(2000);

            try
            {
                File.WriteAllLines(tempFile, entries);

                var wordlist = new Wordlist("Benchmark Wordlist", tempFile, wordlistType, "Diagnostics", countLines: false)
                {
                    Total = entries.Length
                };

                var stopwatch = Stopwatch.StartNew();

                try
                {
                    var dataPool = new WordlistDataPool(wordlist);
                    var enumerated = dataPool.DataList.Take(Math.Min(entries.Length, 1500)).Count();
                    var elapsed = stopwatch.Elapsed;
                    var details = $"Enumerated {enumerated} entry(ies) from disk";
                    return Task.FromResult(BenchmarkResult.SuccessResult(name, elapsed, details));
                }
                catch (Exception ex)
                {
                    var elapsed = stopwatch.Elapsed;
                    return Task.FromResult(BenchmarkResult.Failure(name, elapsed, ex.Message));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(BenchmarkResult.Failure(name, TimeSpan.Zero, ex.Message));
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        private static string[] GenerateBenchmarkWordlistEntries(int count)
        {
            var lines = new string[count];

            for (int i = 0; i < count; i++)
            {
                lines[i] = $"user{i:0000}:password{i:0000}";
            }

            return lines;
        }

        private Task<BenchmarkResult> BenchmarkPluginDiscoveryAsync(BenchmarkContext context)
        {
            const string name = "Plugin catalogue scan";

            if (context.PluginRepository == null)
            {
                return Task.FromResult(BenchmarkResult.SkippedResult(name, "Plugin repository unavailable"));
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var pluginNames = context.PluginRepository.GetPluginNames().ToList();
                var elapsed = stopwatch.Elapsed;
                var preview = pluginNames.Count == 0
                    ? "No plugins detected"
                    : $"Loaded {pluginNames.Count} plugin(s) ({string.Join(", ", pluginNames.Take(3))}{(pluginNames.Count > 3 ? ", ..." : string.Empty)})";

                return Task.FromResult(BenchmarkResult.SuccessResult(name, elapsed, preview));
            }
            catch (Exception ex)
            {
                var elapsed = stopwatch.Elapsed;
                return Task.FromResult(BenchmarkResult.Failure(name, elapsed, ex.Message));
            }
        }

        private (string totalMB, string usedMB, double percentage) GetMemoryUsage()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64;
                var totalMemory = computerInfo.TotalPhysicalMemory;
                var usedMemory = workingSet;
                var percentage = totalMemory > 0 ? (double)usedMemory / totalMemory * 100.0 : 0;

                return (
                    (totalMemory / 1024 / 1024).ToString(),
                    (workingSet / 1024 / 1024).ToString(),
                    percentage
                );
            }
            catch
            {
                return ("N/A", "N/A", 0);
            }
        }

        private double GetCpuUsage()
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var startCpu = Process.GetCurrentProcess().TotalProcessorTime;

                System.Threading.Thread.Sleep(1000); // Wait 1 second

                var endTime = DateTime.UtcNow;
                var endCpu = Process.GetCurrentProcess().TotalProcessorTime;

                var cpuUsedMs = (endCpu - startCpu).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

                return Math.Clamp(cpuUsageTotal * 100.0, 0.0, 100.0);
            }
            catch
            {
                return 0;
            }
        }

        private string GetSystemStatus()
        {
            try
            {
                var (memoryTotal, memoryUsed, memoryPercent) = GetSystemMemoryInfo();
                var cpuUsage = GetCpuUsage();

                if (memoryPercent < 50 && cpuUsage < 50)
                    return "Optimal";
                else if (memoryPercent < 75 && cpuUsage < 75)
                    return "Moderate";
                else
                    return "High Load";
            }
            catch
            {
                return "Unknown";
            }
        }

        private (long totalMemory, long availableMemory, float usagePercent) GetSystemMemoryInfo()
        {
            try
            {
                var computerInfo = new ComputerInfo();
                var totalMemory = (long)computerInfo.TotalPhysicalMemory;
                var availableMemory = (long)computerInfo.AvailablePhysicalMemory;
                var usagePercent = totalMemory == 0 ? 0f : (float)(100.0 * (totalMemory - availableMemory) / totalMemory);

                return (totalMemory, availableMemory, usagePercent);
            }
            catch
            {
                return (0, 0, 0f);
            }
        }

        private Brush GetPerformanceColor(double value)
        {
            if (value < 50) return Brushes.LightGreen;
            if (value < 80) return Brushes.Orange;
            return Brushes.Red;
        }

        private void AppendBenchmarkLog(string message)
        {
            if (BenchmarkOutputTextBox == null) return;
            
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}{Environment.NewLine}";
            
            BenchmarkOutputTextBox.AppendText(logEntry);
            BenchmarkOutputTextBox.ScrollToEnd();
        }

        private void SetBenchmarkStatus(string message, Brush brush)
        {
            BenchmarkStatusTextBlock.Text = message;
            BenchmarkStatusTextBlock.Foreground = brush;
            BenchmarkStatusBorder.Visibility = Visibility.Visible;
        }

        private void InitializePerformanceDisplay()
        {
            try
            {
                // Set initial values quickly without expensive operations
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64;
                var totalMemory = computerInfo.TotalPhysicalMemory;
                
                if (totalMemory > 0)
                {
                    MemoryUsageValue.Text = $"{workingSet / 1024 / 1024} MB / {totalMemory / 1024 / 1024} MB";
                    MemoryUsageTextBlock.Text = "0.0%";
                    MemoryUsageTextBlock.Foreground = Brushes.LightGreen;
                }
                else
                {
                    MemoryUsageValue.Text = "N/A";
                    MemoryUsageTextBlock.Text = "N/A";
                    MemoryUsageTextBlock.Foreground = Brushes.Gray;
                }

                // Set initial CPU value
                CpuUsageValue.Text = "0.0%";
                CpuUsageTextBlock.Text = "LOW";
                CpuUsageTextBlock.Foreground = Brushes.LightGreen;

                // Set initial system status
                SystemStatusValue.Text = "Optimal";
                SystemStatusTextBlock.Text = "GOOD";
                SystemStatusTextBlock.Foreground = Brushes.LightGreen;
            }
            catch
            {
                // Set safe defaults if anything fails
                MemoryUsageValue.Text = "N/A";
                MemoryUsageTextBlock.Text = "N/A";
                CpuUsageValue.Text = "N/A";
                CpuUsageTextBlock.Text = "N/A";
                SystemStatusValue.Text = "Unknown";
                SystemStatusTextBlock.Text = "N/A";
            }
        }

        private void ToolsScrollViewer_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                UpdateCardLayout(element.ActualWidth);
            }
        }

        private void ToolsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCardLayout(e.NewSize.Width);
        }

        private void UpdateCardLayout(double availableWidth)
        {
            if (ToolCardPanel == null)
            {
                return;
            }

            if (double.IsNaN(availableWidth) || availableWidth <= 0)
            {
                return;
            }

            // Account for the scrollbar gutter so the last column does not clip.
            availableWidth = Math.Max(availableWidth - SystemParameters.VerticalScrollBarWidth, CardMinWidth);

            var maxColumns = Math.Max(1, (int)Math.Floor((availableWidth + CardHorizontalSpacing) / (CardMinWidth + CardHorizontalSpacing)));
            maxColumns = Math.Min(maxColumns, CardMaxColumns);

            for (var columns = maxColumns; columns >= 1; columns--)
            {
                var candidate = (availableWidth - (columns - 1) * CardHorizontalSpacing) / columns;
                if (candidate < CardMinWidth)
                {
                    continue;
                }

                ApplyCardWidth(Math.Min(candidate, CardMaxWidth));
                return;
            }

            ApplyCardWidth(Math.Max(CardMinWidth, Math.Min(CardMaxWidth, availableWidth)));
        }

        private void ApplyCardWidth(double width)
        {
            if (double.IsNaN(width) || width <= 0 || ToolCardPanel == null)
            {
                return;
            }

            if (Math.Abs(ToolCardPanel.ItemWidth - width) > 0.5)
            {
                ToolCardPanel.ItemWidth = width;
            }
        }

        #endregion

        private sealed class LineReducerCompareFile
        {
            public LineReducerCompareFile(string fullPath, long length)
            {
                FullPath = fullPath;
                Length = length;
                DisplayName = Path.GetFileName(fullPath);
                Details = $"{FormatBytes(length)} • {fullPath}";
            }

            public string FullPath { get; }
            public long Length { get; }
            public string DisplayName { get; }
            public string Details { get; }
        }

        private sealed record LineReducerResult(
            long ProcessedSourceLines,
            long RemovedLines,
            long WrittenLines,
            long IndexedLines,
            long SourceBytes,
            long ComparisonBytes,
            TimeSpan Elapsed);

        private sealed record LineReductionProgress(
            double Percent,
            string Stage,
            long ProcessedSourceLines,
            long RemovedLines,
            long WrittenLines,
            long IndexedLines);

        private readonly record struct LineReducerOptions(bool TrimWhitespace, bool IgnoreCase);

        private readonly struct LineFingerprint : IEquatable<LineFingerprint>
        {
            public LineFingerprint(ulong primary, ulong secondary, int byteLength)
            {
                Primary = primary;
                Secondary = secondary;
                ByteLength = byteLength;
            }

            public ulong Primary { get; }
            public ulong Secondary { get; }
            public int ByteLength { get; }

            public static LineFingerprint Create(string? line, bool trimWhitespace, bool ignoreCase)
            {
                ReadOnlySpan<char> span = line is null
                    ? ReadOnlySpan<char>.Empty
                    : line.AsSpan();

                if (trimWhitespace)
                {
                    span = span.Trim();
                }

                if (span.Length == 0)
                {
                    return default;
                }

                char[]? rentedChars = null;
                if (ignoreCase)
                {
                    var normalizedLength = span.Length;
                    rentedChars = ArrayPool<char>.Shared.Rent(normalizedLength);
                    for (var i = 0; i < normalizedLength; i++)
                    {
                        rentedChars[i] = char.ToUpperInvariant(span[i]);
                    }

                    span = rentedChars.AsSpan(0, normalizedLength);
                }

                var byteCount = Encoding.UTF8.GetByteCount(span);
                if (byteCount == 0)
                {
                    if (rentedChars is not null)
                    {
                        ArrayPool<char>.Shared.Return(rentedChars);
                    }

                    return default;
                }

                var rentedBytes = ArrayPool<byte>.Shared.Rent(byteCount);
                var bytesWritten = Encoding.UTF8.GetBytes(span, rentedBytes.AsSpan(0, byteCount));

                var fingerprint = new LineFingerprint(
                    XxHash3.HashToUInt64(rentedBytes.AsSpan(0, bytesWritten)),
                    XxHash64.HashToUInt64(rentedBytes.AsSpan(0, bytesWritten)),
                    bytesWritten);

                ArrayPool<byte>.Shared.Return(rentedBytes);
                if (rentedChars is not null)
                {
                    ArrayPool<char>.Shared.Return(rentedChars);
                }

                return fingerprint;
            }

            public bool Equals(LineFingerprint other)
                => Primary == other.Primary && Secondary == other.Secondary && ByteLength == other.ByteLength;

            public override bool Equals(object? obj)
                => obj is LineFingerprint other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(Primary, Secondary, ByteLength);
        }

        private sealed class ToolCardMetadata
        {
            private readonly string[] aliases;
            private readonly string searchHaystack;

            public ToolCardMetadata(Border card, string title, string category, params string[] keywords)
            {
                Card = card ?? throw new ArgumentNullException(nameof(card));
                Title = title;
                Category = category;

                aliases = new[] { title, category }
                    .Concat(keywords ?? Array.Empty<string>())
                    .Select(alias => alias?.Trim())
                    .Where(alias => !string.IsNullOrEmpty(alias))
                    .ToArray()!;

                searchHaystack = string.Join(' ', aliases);
            }

            public Border Card { get; }
            public string Title { get; }
            public string Category { get; }

            public bool HasAlias(string alias)
                => !string.IsNullOrWhiteSpace(alias) &&
                   aliases.Any(a => a.Equals(alias, StringComparison.OrdinalIgnoreCase));

            public bool IsInCategory(string category)
                => string.Equals(Category, category, StringComparison.OrdinalIgnoreCase);

            public bool MatchesSearchTerms(IEnumerable<string> searchTerms)
            {
                if (searchTerms is null)
                {
                    return true;
                }

                foreach (var term in searchTerms)
                {
                    if (!searchHaystack.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
