using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Flux.Native.ViewModels.Base;

namespace Flux.Native.ViewModels.Tools;

public sealed class TextCleanerToolViewModel : ToolCardViewModelBase
{
    private readonly RelayCommand cleanCommand;
    private readonly RelayCommand clearCommand;
    private readonly RelayCommand copyOutputCommand;
    private string input = string.Empty;
    private string output = string.Empty;
    private string statusMessage = string.Empty;
    private Brush statusBrush = Brushes.LightGreen;
    private bool hasStatus;

    public TextCleanerToolViewModel()
        : base("Text Cleaner", "Text", "normalize", "whitespace", "dedupe", "cleanup", "formatter", "text", "sort")
    {
        cleanCommand = new RelayCommand(Clean);
        clearCommand = new RelayCommand(Clear);
        copyOutputCommand = new RelayCommand(CopyOutput, () => !string.IsNullOrWhiteSpace(Output));
    }

    public RelayCommand CleanCommand => cleanCommand;

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

    public void Clean()
    {
        if (string.IsNullOrWhiteSpace(Input))
        {
            Output = string.Empty;
            SetStatus("Input is empty.", Brushes.OrangeRed);
            return;
        }

        var cleanedLines = CleanAndSortLines(Input);
        if (cleanedLines.Count == 0)
        {
            Output = string.Empty;
            SetStatus("No valid lines found.", Brushes.OrangeRed);
            return;
        }

        Output = string.Join(Environment.NewLine, cleanedLines);
        SetStatus($"Processed {cleanedLines.Count} line(s).", Brushes.LawnGreen);
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
            SetStatus("Nothing to copy.", Brushes.OrangeRed);
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
            .OrderBy(entry => GetSortKey(entry.Line))
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Line)
            .ToList();
    }

    private static int GetSortKey(string line)
    {
        var match = Regex.Match(line, "\u2666(\\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var value))
        {
            return value;
        }

        return int.MaxValue;
    }
}
