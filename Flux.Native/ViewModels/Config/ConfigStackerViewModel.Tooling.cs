using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Flux.Native.ViewModels.Base;
using RuriLib.Helpers;
using RuriLib.Helpers.Blocks;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Custom;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Blocks.Settings.Interpolated;

namespace Flux.Native.ViewModels.Configs;

public partial class ConfigStackerViewModel
{
    private readonly List<BlockInstance> clipboardBlocks = new();
    private readonly List<(int index, BlockViewModel blockVm)> lastCloneOperation = new();
    private readonly List<(int index, BlockViewModel blockVm)> lastPasteOperation = new();
    private CancellationTokenSource? searchDebounceCts;
    private string searchText = string.Empty;

    public event Action<BlockViewModel>? SelectionBringIntoViewRequested;

    public event Action<string, string>? NotificationRequested;

    public RelayCommand RemoveSelectedCommand { get; private set; } = null!;

    public RelayCommand MoveSelectedUpCommand { get; private set; } = null!;

    public RelayCommand MoveSelectedDownCommand { get; private set; } = null!;

    public RelayCommand CloneSelectedCommand { get; private set; } = null!;

    public RelayCommand ToggleDisabledCommand { get; private set; } = null!;

    public RelayCommand UndoCommand { get; private set; } = null!;

    public RelayCommand CopyCommand { get; private set; } = null!;

    public RelayCommand PasteCommand { get; private set; } = null!;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value ?? string.Empty))
            {
                ScheduleSearchApply();
            }
        }
    }

    private void InitializeTooling()
    {
        RemoveSelectedCommand = new RelayCommand(ExecuteRemoveSelected, HasSelectedBlocks);
        MoveSelectedUpCommand = new RelayCommand(ExecuteMoveSelectedUp, HasSelectedBlocks);
        MoveSelectedDownCommand = new RelayCommand(ExecuteMoveSelectedDown, HasSelectedBlocks);
        CloneSelectedCommand = new RelayCommand(ExecuteCloneSelected, HasSelectedBlocks);
        ToggleDisabledCommand = new RelayCommand(ExecuteToggleDisabled, HasSelectedBlocks);
        UndoCommand = new RelayCommand(UndoLastOperation, CanUndo);
        CopyCommand = new RelayCommand(CopySelectedBlocks, HasSelectedBlocks);
        PasteCommand = new RelayCommand(PasteBlocks);
    }

    public void HandleBlockClick(BlockViewModel block, bool ctrl, bool shift)
    {
        SelectBlock(block, ctrl, shift);

        if (!string.IsNullOrEmpty(SearchText))
        {
            ClearSearch();
        }

        SelectionBringIntoViewRequested?.Invoke(block);
    }

    public void CreateBlockAndResetUndo(BlockDescriptor descriptor)
    {
        ClearAllUndo();
        CreateBlock(descriptor);
        ClearSearch();
    }

    public void RaiseToolCommandStateChanged()
    {
        RemoveSelectedCommand.RaiseCanExecuteChanged();
        MoveSelectedUpCommand.RaiseCanExecuteChanged();
        MoveSelectedDownCommand.RaiseCanExecuteChanged();
        CloneSelectedCommand.RaiseCanExecuteChanged();
        ToggleDisabledCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
        CopyCommand.RaiseCanExecuteChanged();
        PasteCommand.RaiseCanExecuteChanged();
    }

    private bool HasSelectedBlocks()
        => Stack?.Any(b => b is not null && b.Selected) == true;

    private bool CanUndo()
        => lastCloneOperation.Count > 0 || lastPasteOperation.Count > 0 || deletedBlocks.Count > 0;

    private void ScheduleSearchApply()
    {
        searchDebounceCts?.Cancel();
        searchDebounceCts?.Dispose();

        var cts = new CancellationTokenSource();
        searchDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                if (!cts.IsCancellationRequested)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ApplySearchFilter(searchText);
                        RaiseToolCommandStateChanged();
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cts.Token);
    }

    private void ClearSearch()
    {
        searchDebounceCts?.Cancel();
        searchDebounceCts?.Dispose();
        searchDebounceCts = null;

        searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        ApplySearchFilter(string.Empty);
    }

    private void ExecuteRemoveSelected()
    {
        ClearAllUndo();
        RemoveSelected();
        ClearSearch();
    }

    private void ExecuteMoveSelectedUp()
    {
        ClearAllUndo();
        MoveSelectedUp();
    }

    private void ExecuteMoveSelectedDown()
    {
        ClearAllUndo();
        MoveSelectedDown();
    }

    private void ExecuteCloneSelected()
    {
        ClearPasteUndo();

        var selectedBlocks = Stack?.Where(b => b is not null && b.Selected).ToList() ?? new List<BlockViewModel>();
        if (!selectedBlocks.Any())
        {
            CloneSelected();
            return;
        }

        var originalBlocks = Stack?.ToList() ?? new List<BlockViewModel>();
        var originalCount = originalBlocks.Count;

        CloneSelected();

        lastCloneOperation.Clear();
        var newCount = Stack?.Count ?? 0;
        if (newCount > originalCount && Stack != null)
        {
            for (var i = 0; i < Stack.Count; i++)
            {
                var currentBlock = Stack[i];
                if (currentBlock is not null && !originalBlocks.Contains(currentBlock))
                {
                    lastCloneOperation.Add((i, currentBlock));
                }
            }
        }

        ClearSearch();
        RaiseToolCommandStateChanged();
    }

    private void ExecuteToggleDisabled()
    {
        ClearPasteUndo();
        EnableDisableSelected();
        RaiseToolCommandStateChanged();
    }

    private void CopySelectedBlocks()
    {
        var selectedBlocks = Stack?.Where(b => b is not null && b.Selected).ToList();
        if (selectedBlocks == null || !selectedBlocks.Any())
        {
            clipboardBlocks.Clear();
            return;
        }

        clipboardBlocks.Clear();
        clipboardBlocks.AddRange(selectedBlocks
            .Where(block => block.Block != null)
            .Select(block => Cloner.Clone<BlockInstance>(block.Block)));

        try
        {
            var clipboardText = string.Join(Environment.NewLine + Environment.NewLine, selectedBlocks.Select(block => CreateDetailedBlockText(block.Block)));
            Clipboard.SetText(clipboardText);
        }
        catch
        {
        }

        ShowNotification("Copy", $"Copied {clipboardBlocks.Count} block(s)");
    }

    private static string CreateDetailedBlockText(BlockInstance block)
    {
        if (block is null)
        {
            return "Unknown Block";
        }

        if (block is LoliCodeBlockInstance loliBlock)
        {
            return loliBlock.Script ?? string.Empty;
        }

        if (block is ScriptBlockInstance || block is KeycheckBlockInstance || block is ParseBlockInstance || block is HttpRequestBlockInstance)
        {
            try
            {
                var loliCode = block.ToLC(true);
                return block is not LoliCodeBlockInstance ? $"BLOCK:{block.Id}\n{loliCode}ENDBLOCK" : loliCode;
            }
            catch
            {
                return string.Empty;
            }
        }

        var details = new List<string>();
        var blockType = block.Descriptor?.Name ?? "Unknown";
        var label = !string.IsNullOrEmpty(block.Label) ? block.Label : blockType;

        details.Add($"BLOCK: {blockType}");
        details.Add($"LABEL: {label}");

        if (block.Disabled)
        {
            details.Add("DISABLED: true");
        }

        AddBlockSettingsDetails(block, details);
        return string.Join(Environment.NewLine, details);
    }

    private static void AddBlockSettingsDetails(BlockInstance block, List<string> details)
    {
        if (block.Settings == null)
        {
            return;
        }

        var importantSettings = new[] { "url", "method", "input", "script", "leftDelim", "rightDelim", "content", "username", "password" };
        var addedSettings = 0;

        foreach (var settingName in importantSettings)
        {
            if (addedSettings >= 5)
            {
                break;
            }

            if (block.Settings.TryGetValue(settingName, out var setting))
            {
                var value = GetSettingDisplayValue(setting);
                if (!string.IsNullOrEmpty(value))
                {
                    details.Add($"{settingName}: {value}");
                    addedSettings++;
                }
            }
        }

        foreach (var setting in block.Settings)
        {
            if (addedSettings >= 8 || importantSettings.Contains(setting.Key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = GetSettingDisplayValue(setting.Value);
            if (!string.IsNullOrEmpty(value))
            {
                details.Add($"{setting.Key}: {value}");
                addedSettings++;
            }
        }
    }

    private static string GetSettingDisplayValue(BlockSetting setting)
    {
        return setting.InputMode switch
        {
            SettingInputMode.Variable => $"@{setting.InputVariableName}",
            SettingInputMode.Interpolated => setting.InterpolatedSetting is InterpolatedStringSetting interpolatedString
                ? interpolatedString.Value
                : string.Empty,
            _ => setting.FixedSetting switch
            {
                RuriLib.Models.Blocks.Settings.StringSetting stringSetting => stringSetting.Value,
                RuriLib.Models.Blocks.Settings.IntSetting intSetting => intSetting.Value.ToString(),
                RuriLib.Models.Blocks.Settings.BoolSetting boolSetting => boolSetting.Value.ToString(),
                _ => string.Empty
            }
        };
    }

    private void PasteBlocks()
    {
        var (blocksToPaste, isFromSystemClipboard) = GetBlocksToPasteFromClipboard();
        if (!blocksToPaste.Any())
        {
            ShowNotification("Paste", "Clipboard does not contain valid block data");
            return;
        }

        var insertIndex = GetPasteInsertionIndex();
        var undoInfo = new List<(int index, BlockViewModel blockVm)>();
        PerformBlockPasting(blocksToPaste, insertIndex, undoInfo);
        ClearSearch();

        var source = isFromSystemClipboard ? "system clipboard" : "internal clipboard";
        ShowNotification("Paste", $"Pasted {blocksToPaste.Count} block(s) from {source}");
    }

    private (List<BlockInstance> blocks, bool isFromSystemClipboard) GetBlocksToPasteFromClipboard()
    {
        var blocksToPaste = new List<BlockInstance>();
        var isFromSystemClipboard = false;

        try
        {
            if (Clipboard.ContainsText())
            {
                var clipboardText = Clipboard.GetText();
                var parsedBlocks = ParseBlocksFromText(clipboardText);

                if (parsedBlocks.Any())
                {
                    blocksToPaste = parsedBlocks;
                    isFromSystemClipboard = true;
                    clipboardBlocks.Clear();
                }
            }
        }
        catch
        {
        }

        if (!blocksToPaste.Any() && clipboardBlocks.Any())
        {
            blocksToPaste = clipboardBlocks.Select(block => Cloner.Clone<BlockInstance>(block)).ToList();
            isFromSystemClipboard = false;
        }

        return (blocksToPaste, isFromSystemClipboard);
    }

    private int GetPasteInsertionIndex()
    {
        var selectedBlocks = Stack?.Where(b => b is not null && b.Selected).ToList() ?? new List<BlockViewModel>();
        if (!selectedBlocks.Any())
        {
            return Stack?.Count ?? 0;
        }

        return Stack!.ToList().FindLastIndex(block => selectedBlocks.Contains(block)) + 1;
    }

    private void PerformBlockPasting(List<BlockInstance> blocksToPaste, int insertIndex, List<(int index, BlockViewModel blockVm)> undoInfo)
    {
        var pastedBlocks = new List<BlockViewModel>();

        foreach (var blockToPaste in blocksToPaste)
        {
            var newBlockVm = new BlockViewModel(blockToPaste);
            if (insertIndex >= 0 && insertIndex <= (Stack?.Count ?? 0))
            {
                Stack?.Insert(insertIndex, newBlockVm);
                pastedBlocks.Add(newBlockVm);
                undoInfo.Add((insertIndex, newBlockVm));
                insertIndex++;
            }
        }

        if (!pastedBlocks.Any())
        {
            return;
        }

        RecordPasteForUndo(undoInfo);
        foreach (var pastedBlock in pastedBlocks)
        {
            pastedBlock.Selected = true;
        }

        if (Stack != null)
        {
            configService.SelectedConfig.Stack = Stack
                .Where(block => block is not null && block.Block is not null)
                .Select(block => block.Block)
                .ToList();
        }

        RaiseToolCommandStateChanged();
    }

    private void RecordPasteForUndo(List<(int index, BlockViewModel blockVm)> pasteInfo)
    {
        ClearCloneUndo();
        lastPasteOperation.Clear();
        lastPasteOperation.AddRange(pasteInfo);
        RaiseToolCommandStateChanged();
    }

    private void UndoLastOperation()
    {
        if (lastCloneOperation.Any())
        {
            foreach (var (index, blockVm) in lastCloneOperation.OrderByDescending(item => item.index))
            {
                if (Stack != null && index >= 0 && index < Stack.Count && Stack[index] == blockVm)
                {
                    Stack.RemoveAt(index);
                }
            }

            if (Stack != null)
            {
                configService.SelectedConfig.Stack = Stack
                    .Where(block => block is not null && block.Block is not null)
                    .Select(block => block.Block)
                    .ToList();
            }

            lastCloneOperation.Clear();
            ShowNotification("Undo", "Clone operation undone");
            RaiseToolCommandStateChanged();
            return;
        }

        if (lastPasteOperation.Any())
        {
            foreach (var (index, blockVm) in lastPasteOperation.OrderByDescending(item => item.index))
            {
                if (Stack != null && index >= 0 && index < Stack.Count && Stack[index] == blockVm)
                {
                    Stack.RemoveAt(index);
                }
            }

            if (Stack != null)
            {
                configService.SelectedConfig.Stack = Stack
                    .Where(block => block is not null && block.Block is not null)
                    .Select(block => block.Block)
                    .ToList();
            }

            lastPasteOperation.Clear();
            ShowNotification("Undo", "Paste operation undone");
            RaiseToolCommandStateChanged();
            return;
        }

        Undo();
        RaiseToolCommandStateChanged();
    }

    private static List<BlockInstance> ParseBlocksFromText(string text)
    {
        var blocks = new List<BlockInstance>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return blocks;
        }

        var blockTexts = text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var blockText in blockTexts)
        {
            var block = TryCreateBlockFromDetailedText(blockText);
            if (block != null)
            {
                blocks.Add(block);
            }
        }

        if (blocks.Any())
        {
            return blocks;
        }

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine))
            {
                continue;
            }

            var block = TryCreateBlockFromText(trimmedLine);
            if (block != null)
            {
                blocks.Add(block);
            }
        }

        return blocks;
    }

    private static BlockInstance? TryCreateBlockFromDetailedText(string text)
    {
        try
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (!lines.Any())
            {
                return null;
            }

            return lines[0].Trim().StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase)
                ? ParseBlockFromBlockIdFormat(lines)
                : ParseBlockFromFallbackFormat(lines);
        }
        catch
        {
            return null;
        }
    }

    private static BlockInstance? ParseBlockFromBlockIdFormat(string[] lines)
    {
        var blockId = lines[0].Trim()[6..].Trim();
        var blockContent = new List<string>();
        var foundEndBlock = false;

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line == "ENDBLOCK")
            {
                foundEndBlock = true;
                break;
            }

            blockContent.Add(lines[i]);
        }

        if (!foundEndBlock)
        {
            return null;
        }

        try
        {
            var block = BlockFactory.GetBlock<BlockInstance>(blockId);
            var contentScript = string.Join(Environment.NewLine, blockContent);
            var lineNumber = 0;
            block.FromLC(ref contentScript, ref lineNumber);
            return block;
        }
        catch
        {
            return null;
        }
    }

    private static BlockInstance? ParseBlockFromFallbackFormat(string[] lines)
    {
        string? blockType = null;
        string? label = null;
        var disabled = false;
        var settings = new Dictionary<string, string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase))
            {
                blockType = trimmedLine[6..].Trim();
            }
            else if (trimmedLine.StartsWith("LABEL:", StringComparison.OrdinalIgnoreCase))
            {
                label = trimmedLine[6..].Trim();
            }
            else if (trimmedLine.StartsWith("DISABLED:", StringComparison.OrdinalIgnoreCase))
            {
                disabled = trimmedLine[9..].Trim().ToLower() == "true";
            }
            else if (trimmedLine.Contains(':') && !trimmedLine.StartsWith("  ", StringComparison.OrdinalIgnoreCase))
            {
                var colonIndex = trimmedLine.IndexOf(':');
                settings[trimmedLine[..colonIndex].Trim()] = trimmedLine[(colonIndex + 1)..].Trim();
            }
        }

        if (string.IsNullOrEmpty(blockType))
        {
            return null;
        }

        var fallbackBlock = CreateFallbackBlock(blockType);
        if (fallbackBlock is null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(label))
        {
            fallbackBlock.Label = label;
        }

        fallbackBlock.Disabled = disabled;
        ApplySettingsToBlock(fallbackBlock, settings);
        return fallbackBlock;
    }

    private static BlockInstance? CreateFallbackBlock(string blockType)
    {
        try
        {
            var blockId = blockType switch
            {
                "Http Request" => "HttpRequest",
                "Parse" => "Parse",
                "Keycheck" => "Keycheck",
                "LoliCode" => "LoliCode",
                _ => blockType
            };

            return BlockFactory.GetBlock<BlockInstance>(blockId);
        }
        catch
        {
            try
            {
                return BlockFactory.GetBlock<AutoBlockInstance>(blockType);
            }
            catch
            {
                return null;
            }
        }
    }

    private static void ApplySettingsToBlock(BlockInstance block, Dictionary<string, string> settings)
    {
        if (block.Settings == null)
        {
            return;
        }

        foreach (var setting in settings)
        {
            if (block.Settings.TryGetValue(setting.Key.ToLower(), out var blockSetting))
            {
                HandleSettingValue(blockSetting, setting.Value);
            }
        }
    }

    private static void HandleSettingValue(BlockSetting blockSetting, string value)
    {
        if (value.StartsWith('@'))
        {
            blockSetting.InputMode = SettingInputMode.Variable;
            blockSetting.InputVariableName = value[1..];
            return;
        }

        blockSetting.InputMode = SettingInputMode.Fixed;
        if (blockSetting.FixedSetting is RuriLib.Models.Blocks.Settings.StringSetting stringSetting)
        {
            stringSetting.Value = value;
        }
        else if (blockSetting.FixedSetting is RuriLib.Models.Blocks.Settings.IntSetting intSetting && int.TryParse(value, out var intValue))
        {
            intSetting.Value = intValue;
        }
        else if (blockSetting.FixedSetting is RuriLib.Models.Blocks.Settings.BoolSetting boolSetting && bool.TryParse(value, out var boolValue))
        {
            boolSetting.Value = boolValue;
        }
        else
        {
            blockSetting.InputMode = SettingInputMode.Interpolated;
            blockSetting.InterpolatedSetting = new InterpolatedStringSetting { Value = value };
        }
    }

    private static BlockInstance? TryCreateBlockFromText(string text)
    {
        try
        {
            BlockInstance block;

            if (text.StartsWith("REQUEST", StringComparison.OrdinalIgnoreCase) || text.Contains("http", StringComparison.OrdinalIgnoreCase))
            {
                block = BlockFactory.GetBlock<HttpRequestBlockInstance>("HttpRequest");
            }
            else if (text.StartsWith("PARSE", StringComparison.OrdinalIgnoreCase) || text.Contains("regex", StringComparison.OrdinalIgnoreCase))
            {
                block = BlockFactory.GetBlock<ParseBlockInstance>("Parse");
            }
            else if (text.StartsWith("KEYCHECK", StringComparison.OrdinalIgnoreCase) || text.Contains("keycheck", StringComparison.OrdinalIgnoreCase))
            {
                block = BlockFactory.GetBlock<KeycheckBlockInstance>("Keycheck");
            }
            else
            {
                var loliCodeBlock = BlockFactory.GetBlock<LoliCodeBlockInstance>("LoliCode");
                loliCodeBlock.Script = text;
                block = loliCodeBlock;
            }

            block.Label = text.Length > 50 ? text[..50] + "..." : text;
            return block;
        }
        catch
        {
            return null;
        }
    }

    private void ShowNotification(string title, string message)
    {
        NotificationRequested?.Invoke(title, message);
    }

    private void ClearPasteUndo()
    {
        lastPasteOperation.Clear();
        RaiseToolCommandStateChanged();
    }

    private void ClearCloneUndo()
    {
        lastCloneOperation.Clear();
        RaiseToolCommandStateChanged();
    }

    private void ClearAllUndo()
    {
        ClearPasteUndo();
        ClearCloneUndo();
    }
}
