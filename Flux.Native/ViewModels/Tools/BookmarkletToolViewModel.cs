using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Flux.Native.ViewModels.Base;

namespace Flux.Native.ViewModels.Tools;

public sealed class BookmarkletToolViewModel : ToolCardViewModelBase
{
    private static readonly Encoding LegacyEncoding;
    private const char DiamondGlyph = '\u2666';
    private const char SpadeGlyph = '\u2660';
    private const string LegacyDiamondMarker = "\u00C3\u00A2\u00E2\u201E\u00A2\u00C2\u00A6";
    private const string LegacySpadeMarker = "\u00C3\u00A2\u00E2\u201E\u00A2\u00C2\u00A0";

    private readonly RelayCommand parseCommand;
    private readonly RelayCommand clearCommand;
    private readonly RelayCommand copyOutputCommand;
    private string input = string.Empty;
    private string output = string.Empty;
    private string statusMessage = string.Empty;
    private Brush statusBrush = Brushes.LightGreen;
    private bool hasStatus;

    static BookmarkletToolViewModel()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        LegacyEncoding = Encoding.GetEncoding(1252);
    }

    public BookmarkletToolViewModel()
        : base("Bookmarklet Parser", "Automation", "javascript", "bookmark", "parser", "payload", "scrubber", "deobfuscate")
    {
        parseCommand = new RelayCommand(Parse);
        clearCommand = new RelayCommand(Clear);
        copyOutputCommand = new RelayCommand(CopyOutput, () => !string.IsNullOrWhiteSpace(Output));
    }

    public RelayCommand ParseCommand => parseCommand;

    public RelayCommand ClearCommand => clearCommand;

    public RelayCommand CopyOutputCommand => copyOutputCommand;

    public string Input
    {
        get => input;
        set => SetProperty(ref input, value ?? string.Empty);
    }

    public string Output
    {
        get => output;
        private set
        {
            if (SetProperty(ref output, value ?? string.Empty))
            {
                copyOutputCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public Brush StatusBrush
    {
        get => statusBrush;
        private set => SetProperty(ref statusBrush, value);
    }

    public bool HasStatus
    {
        get => hasStatus;
        private set
        {
            if (SetProperty(ref hasStatus, value))
            {
                OnPropertyChanged(nameof(StatusVisibility));
            }
        }
    }

    public Visibility StatusVisibility => HasStatus ? Visibility.Visible : Visibility.Collapsed;

    public void Parse()
    {
        var raw = Input.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            Output = string.Empty;
            SetStatus("Input is empty.", Brushes.OrangeRed);
            return;
        }

        var lines = raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
            .ToArray();

        if (lines.Length == 0)
        {
            Output = string.Empty;
            SetStatus("No usable lines.", Brushes.OrangeRed);
            return;
        }

        var results = lines.Select(TryParseBookmarkletLine).ToArray();
        Output = string.Join(Environment.NewLine + Environment.NewLine, results);
        SetStatus($"Parsed {lines.Length} line(s).", Brushes.LawnGreen);
    }

    public void Clear()
    {
        Input = string.Empty;
        Output = string.Empty;
        HasStatus = false;
        StatusMessage = string.Empty;
    }

    public void CopyOutput()
    {
        if (string.IsNullOrWhiteSpace(Output))
        {
            return;
        }

        try
        {
            Clipboard.SetText(Output);
            SetStatus("Output copied to clipboard.", Brushes.LawnGreen);
        }
        catch (Exception ex)
        {
            SetStatus($"Unable to copy output: {ex.Message}", Brushes.OrangeRed);
        }
    }

    private void SetStatus(string message, Brush brush)
    {
        StatusMessage = message;
        StatusBrush = brush;
        HasStatus = true;
    }

    private static string TryParseBookmarkletLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return "Invalid input";
        }

        line = NormalizeBookmarkletLine(line);

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

    private static string NormalizeBookmarkletLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        var normalized = line;

        if (normalized.IndexOf('?', StringComparison.Ordinal) >= 0)
        {
            var bytes = LegacyEncoding.GetBytes(normalized);
            normalized = Encoding.UTF8.GetString(bytes);
        }

        normalized = normalized
            .Replace(LegacyDiamondMarker, DiamondGlyph.ToString(), StringComparison.Ordinal)
            .Replace(LegacySpadeMarker, SpadeGlyph.ToString(), StringComparison.Ordinal)
            .Replace('\u00A0', ' ');

        return normalized.Trim();
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

        var post = Regex.Match(line, @$"^(\d+){DiamondGlyph}").Groups[1].Value;
        var follower = Regex.Match(line, "(\\d+)~").Groups[1].Value;
        var year = Regex.Match(line, "~\\s*(\\d+)").Groups[1].Value;
        var passwordLine = emailSuffix != null ? $"charming@{emailSuffix}" : "N/A";

        var builder = new StringBuilder();
        builder.AppendLine(email);
        builder.AppendLine($"password: {passwordLine}");
        builder.AppendLine($"check email: akunlama.com/inbox/{usernamePart}");
        builder.AppendLine($"auth_token={authToken ?? "N/A"}");
        builder.AppendLine();
        builder.Append($"Username?Post?Follower?Tahun = {username ?? "N/A"}?{post}");
        builder.Append($"?{(string.IsNullOrEmpty(follower) ? "N/A" : follower)}");
        builder.Append($"?{(string.IsNullOrEmpty(year) ? "N/A" : year)}");
        return builder.ToString();
    }

    private static string? ExtractDetailedPatternLine(string line)
    {
        if (string.IsNullOrEmpty(line) || !line.Contains('='))
        {
            return null;
        }

        var pattern = new Regex(
            $@"^(?<user>.+?){DiamondGlyph}\s*(?<posts>\d+)\s*\*(?<count>\d+)\s*{SpadeGlyph}\s*(?<year>\d+)\s*@(?<handle>[^\s=]+)\s*=(?<session>\S+)(?:\s+(?<tail>.*))?$",
            RegexOptions.CultureInvariant);

        var match = pattern.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var rawUsername = match.Groups["user"].Value.Trim();
        var handle = match.Groups["handle"].Value.Trim();
        var sessionId = match.Groups["session"].Value.Trim();
        var password = rawUsername.Length >= 3
            ? rawUsername[^3..] + "@asem777"
            : $"{rawUsername}@asem777";
        string? twoFaSecret = null;

        var tail = match.Groups["tail"].Value?.Trim();
        if (!string.IsNullOrWhiteSpace(tail))
        {
            var twoFaMatch = Regex.Match(tail, "2FA:(.*)", RegexOptions.IgnoreCase);
            if (twoFaMatch.Success)
            {
                var before = tail[..twoFaMatch.Index].Trim();
                if (!string.IsNullOrEmpty(before))
                {
                    password = before;
                }

                twoFaSecret = twoFaMatch.Groups[1].Value.Trim();
            }
            else
            {
                password = tail;
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
        var post = Regex.Match(line, @$"^(\d+){DiamondGlyph}").Groups[1].Value;
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
            builder.Append(
                $"Username?Post?Follower?Tahun = {(string.IsNullOrEmpty(username) ? "N/A" : username)}?{(string.IsNullOrEmpty(post) ? "N/A" : post)}?{(string.IsNullOrEmpty(follower) ? "N/A" : follower)}?{(string.IsNullOrEmpty(year) ? "N/A" : year)}");
        }

        return builder.ToString();
    }
}
