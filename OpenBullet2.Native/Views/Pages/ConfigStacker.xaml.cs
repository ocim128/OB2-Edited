using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Controls;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.Views.Dialogs;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Custom;
using RuriLib.Models.Configs;
using RuriLib.Helpers;
using RuriLib.Helpers.Blocks;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Blocks.Settings.Interpolated;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls; // Make sure this is included for TextBox and TextChangedEventArgs
using System.Windows.Input;
using OpenBullet2.Native.Infrastructure.DependencyInjection;

namespace OpenBullet2.Native.Views.Pages
{
    /// <summary>
    /// Interaction logic for ConfigStacker.xaml
    /// </summary>
    public partial class ConfigStacker : Page
    {
        private readonly ConfigService configService;
        private readonly ConfigStackerViewModel vm;

        // Clipboard functionality for copy/paste blocks
        private List<BlockInstance> clipboardBlocks = new List<BlockInstance>();

        // Track last clone operation for undo
        private List<(int index, BlockViewModel blockVm)> lastCloneOperation = new List<(int, BlockViewModel)>();

        public ConfigStacker()
        {
            configService = ServiceLocator.GetService<ConfigService>();
            vm = ServiceLocator.GetService<ViewModelsService>().ConfigStacker;
            vm.SelectionChanged += SelectionChanged;
            DataContext = vm;

            InitializeComponent();

            // Ensure the page can receive keyboard focus for copy/paste shortcuts
            Loaded += (s, e) => Focus();
        }

        public void UpdateViewModel()
        {
            try
            {
                // Try to change the mode to Stack
                configService.SelectedConfig.ChangeMode(ConfigMode.Stack);
            }
            catch (Exception ex)
            {
                // On fail, prompt it to the user and go back to the configs page
                Alert.Exception(ex);
                ServiceLocator.GetService<MainWindow>().NavigateTo(MainWindowPage.Configs);
            }

            vm.UpdateViewModel();
            if (vm.Stack?.Any() == true)
            {
                vm.SelectBlock(vm.Stack.First(), false);
            }
        }

        public void CreateBlock(BlockDescriptor descriptor)
        {
            ClearAllUndo(); // Clear all undo when adding new blocks
            vm.CreateBlock(descriptor);
        }

        private void AddBlock(object sender, RoutedEventArgs e)
            => new MainDialog(new AddBlockDialog(this), "Add block", 760, 620).ShowDialog();

        private void CopyBlocks(object sender, RoutedEventArgs e)
        {
            CopySelectedBlocks();
            e.Handled = true;
        }

        private void PasteBlocksFromClipboard(object sender, RoutedEventArgs e)
        {
            PasteBlocks();
            e.Handled = true;
        }

        private void RemoveBlock(object sender, RoutedEventArgs e)
        {
            ClearAllUndo(); // Clear all undo when doing other operations
            vm.RemoveSelected();
        }

        private void MoveBlockUp(object sender, RoutedEventArgs e)
        {
            ClearAllUndo(); // Clear all undo when doing other operations
            vm.MoveSelectedUp();
        }

        private void MoveBlockDown(object sender, RoutedEventArgs e)
        {
            ClearAllUndo(); // Clear all undo when doing other operations
            vm.MoveSelectedDown();
        }

        private void CloneBlock(object sender, RoutedEventArgs e)
        {
            ClearPasteUndo(); // Clear paste undo when doing clone operation

            // Record the current state before cloning
            var selectedBlocks = vm.Stack?.Where(b => b != null && b.Selected).ToList() ?? new List<BlockViewModel>();

            if (!selectedBlocks.Any())
            {
                vm.CloneSelected();
                return;
            }

            // Store the exact BlockViewModel references before cloning
            var originalBlocks = vm.Stack?.ToList() ?? new List<BlockViewModel>();
            var originalCount = originalBlocks.Count;

            // Perform the clone operation
            vm.CloneSelected();

            // Find the newly added blocks by comparing references
            var cloneInfo = new List<(int index, BlockViewModel blockVm)>();
            var newCount = vm.Stack?.Count ?? 0;

            if (newCount > originalCount && vm.Stack != null)
            {
                // Find blocks that are new (not in the original reference list)
                for (int i = 0; i < vm.Stack.Count; i++)
                {
                    var currentBlock = vm.Stack[i];
                    if (currentBlock != null && !originalBlocks.Contains(currentBlock))
                    {
                        // This is a new block that wasn't in the original stack
                        cloneInfo.Add((i, currentBlock));
                    }
                }
            }

            // Store for undo
            lastCloneOperation = cloneInfo;

            System.Diagnostics.Debug.WriteLine($"Recorded {cloneInfo.Count} cloned blocks for undo");
        }

        private void EnableDisableBlock(object sender, RoutedEventArgs e)
        {
            ClearPasteUndo(); // Clear paste undo when doing other operations
            vm.EnableDisableSelected();
        }

        private void Undo(object sender, RoutedEventArgs e)
        {
            UndoLastOperation();
        }

        private void SelectBlock(object sender, MouseEventArgs e) => SelectBlock(sender);
        private void SelectBlock(object sender, RoutedEventArgs e) => SelectBlock(sender);
        private void SelectBlock(object sender)
        {
            var ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            var shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            var block = (BlockViewModel)(sender as FrameworkElement).Tag;
            vm.SelectBlock(block, ctrl, shift);
            // Clear search filter to show all blocks
            SearchTextBox.Text = string.Empty;
            // Force layout update and scroll immediately
            BlocksItemsControl.UpdateLayout();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                BlocksItemsControl.UpdateLayout();
                var container = BlocksItemsControl.ItemContainerGenerator.ContainerFromItem(block) as FrameworkElement;
                container?.BringIntoView();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void SelectionChanged(IEnumerable<BlockViewModel> selected)
        {
            var first = selected.FirstOrDefault();

            if (first is null)
            {
                BlockInfoContent.Content = null;
                BlockInfoPlaceholder.Visibility = Visibility.Visible;
            }
            else
            {
                UserControl content = first.Block switch
                {
                    AutoBlockInstance => new AutoBlockSettingsViewer(first),
                    ParseBlockInstance => new ParseBlockSettingsViewer(first),
                    ScriptBlockInstance => new ScriptBlockSettingsViewer(first),
                    HttpRequestBlockInstance => new HttpRequestBlockSettingsViewer(first),
                    KeycheckBlockInstance => new KeycheckBlockSettingsViewer(first),
                    LoliCodeBlockInstance => new LoliCodeBlockSettingsViewer(first),
                    _ => null
                };

                BlockInfoContent.Content = content;
                BlockInfoPlaceholder.Visibility = content is null ? Visibility.Visible : Visibility.Collapsed;

                if (content != null)
                {
                    BlockInfoScrollViewer.ScrollToHome();
                }
            }
        }

        private void PageKeyDown(object sender, KeyEventArgs e)
        {
            // Copy functionality (Ctrl+C)
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                CopySelectedBlocks();
                e.Handled = true;
            }
            // Paste functionality (Ctrl+V)
            else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                PasteBlocks();
                e.Handled = true;
            }
            // Undo functionality (Ctrl+Z) - only if blocks are selected or page has focus
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Only handle Ctrl+Z if:
                // 1. A block is selected, OR
                // 2. The page/stacker has focus (not a text input or other control)
                var hasSelectedBlocks = vm.Stack?.Any(b => b != null && b.Selected) == true;
                var pageHasFocus = IsKeyboardFocusWithin || IsFocused;
                var focusedElement = Keyboard.FocusedElement;

                // Don't handle if focus is on a text input (TextBox, etc.)
                var isTextInputFocused = focusedElement is TextBox ||
                                       focusedElement is System.Windows.Controls.RichTextBox ||
                                       focusedElement?.GetType().Name.Contains("TextBox") == true;

                if ((hasSelectedBlocks || pageHasFocus) && !isTextInputFocused)
                {
                    UndoLastOperation();
                    e.Handled = true;
                }
            }
        }

        // <-- ADDED --> Method to handle text changes in the search box
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = (sender as TextBox).Text;
            vm.ApplySearchFilter(searchText); // <-- Uncommented line
        }
        // <-- ADDED -->

        // Clipboard functionality for copy/paste blocks
        private void CopySelectedBlocks()
        {
            var selectedBlocks = vm.Stack?.Where(b => b != null && b.Selected).ToList();
            if (selectedBlocks == null || !selectedBlocks.Any())
            {
                clipboardBlocks.Clear();
                return;
            }

            // Clone the selected blocks to clipboard
            clipboardBlocks = selectedBlocks
                .Where(b => b.Block != null)
                .Select(b => Cloner.Clone<BlockInstance>(b.Block))
                .ToList();

            // Create detailed clipboard text with block settings
            try
            {
                var blockTexts = selectedBlocks.Select(b => CreateDetailedBlockText(b.Block)).ToList();
                var clipboardText = string.Join(Environment.NewLine + Environment.NewLine, blockTexts);
                Clipboard.SetText(clipboardText);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set system clipboard: {ex.Message}");
            }

            // Show auto-dismissing notification (non-blocking)
            ShowAutoNotification("Copy", $"Copied {clipboardBlocks.Count} block(s)");
        }

        private static string CreateDetailedBlockText(BlockInstance block)
        {
            if (block == null) return "Unknown Block";

            if (block is LoliCodeBlockInstance loliBlock)
            {
                return GetLoliCodeBlockText(loliBlock);
            }

            if (block is ScriptBlockInstance || block is KeycheckBlockInstance || block is ParseBlockInstance || block is HttpRequestBlockInstance)
            {
                return GetComplexBlockText(block);
            }

            return GetBasicBlockText(block);
        }

        private static string GetLoliCodeBlockText(LoliCodeBlockInstance loliBlock)
        {
            return loliBlock.Script ?? "";
        }

        private static string GetComplexBlockText(BlockInstance block)
        {
            try
            {
                var loliCode = block.ToLC(true);
                return block is not LoliCodeBlockInstance ? $"BLOCK:{block.Id}\n{loliCode}ENDBLOCK" : loliCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get ToLC for {block.Id}: {ex.Message}");
                return string.Empty;
            }
        }

        private static string GetBasicBlockText(BlockInstance block)
        {
            var details = new List<string>();
            var blockType = block.Descriptor?.Name ?? "Unknown";
            var label = !string.IsNullOrEmpty(block.Label) ? block.Label : blockType;

            details.Add($"BLOCK: {blockType}");
            details.Add($"LABEL: {label}");

            if (block.Disabled)
                details.Add("DISABLED: true");

            AddBlockSettingsDetails(block, details);

            return string.Join(Environment.NewLine, details);
        }

        private static void AddBlockSettingsDetails(BlockInstance block, List<string> details)
        {
            if (block.Settings == null) return;

            var importantSettings = new[] { "url", "method", "input", "script", "leftDelim", "rightDelim", "content", "username", "password" };
            var addedSettings = 0;

            AddImportantSettings(block, details, importantSettings, ref addedSettings);
            AddOtherSettings(block, details, importantSettings, ref addedSettings);
        }

        private static void AddImportantSettings(
            BlockInstance block,
            List<string> details,
            string[] importantSettings,
            ref int addedSettings)
        {
            foreach (var settingName in importantSettings)
            {
                if (addedSettings >= 5) break;

                if (block.Settings.TryGetValue(settingName, out var setting))
                {
                    var value = GetSettingDisplayValue(setting);
                    if (!string.IsNullOrEmpty(value))
                    {
                        details.Add($"{settingName.ToUpper()}: {value}");
                        addedSettings++;
                    }
                }
            }
        }

        private static void AddOtherSettings(
            BlockInstance block,
            List<string> details,
            string[] importantSettings,
            ref int addedSettings)
        {
            if (addedSettings < 3)
            {
                foreach (var kvp in block.Settings.Take(5))
                {
                    if (addedSettings >= 5) break;

                    if (!importantSettings.Contains(kvp.Key))
                    {
                        var value = GetSettingDisplayValue(kvp.Value);
                        if (!string.IsNullOrEmpty(value))
                        {
                            details.Add($"{kvp.Key.ToUpper()}: {value}");
                            addedSettings++;
                        }
                    }
                }
            }
        }

        private static string GetSettingDisplayValue(BlockSetting setting)
        {
            if (setting == null) return "";

            try
            {
                // Get the value based on input mode
                switch (setting.InputMode)
                {
                    case RuriLib.Models.Blocks.Settings.SettingInputMode.Fixed:
                        return setting.FixedSetting?.ToString() ?? "";
                    case RuriLib.Models.Blocks.Settings.SettingInputMode.Variable:
                        return $"@{setting.InputVariableName}";
                    case RuriLib.Models.Blocks.Settings.SettingInputMode.Interpolated:
                        return setting.InterpolatedSetting?.ToString() ?? "";
                    default:
                        return "";
                }
            }
            catch
            {
                return "";
            }
        }

        private void PasteBlocks()
        {
            var (blocksToPaste, isFromSystemClipboard) = GetBlocksToPasteFromClipboard();

            if (!blocksToPaste.Any())
            {
                ShowAutoNotification("Paste", "No blocks to paste");
                return;
            }

            var undoInfo = new List<(int index, BlockViewModel blockVm)>();
            var insertIndex = GetPasteInsertionIndex();

            PerformBlockPasting(blocksToPaste, insertIndex, undoInfo);

            // Show auto-dismissing notification
            var source = isFromSystemClipboard ? "system clipboard" : "internal clipboard";
            ShowAutoNotification("Paste", $"Pasted {blocksToPaste.Count} block(s) from {source}");
        }

        private (List<BlockInstance> blocks, bool isFromSystemClipboard) GetBlocksToPasteFromClipboard()
        {
            List<BlockInstance> blocksToPaste = new List<BlockInstance>();
            bool isFromSystemClipboard = false;

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
                        clipboardBlocks.Clear(); // Clear internal clipboard since we're using external content
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to access system clipboard: {ex.Message}");
            }

            if (!blocksToPaste.Any() && clipboardBlocks.Any())
            {
                blocksToPaste = clipboardBlocks.Select(b => Cloner.Clone<BlockInstance>(b)).ToList();
                isFromSystemClipboard = false;
            }

            return (blocksToPaste, isFromSystemClipboard);
        }

        private int GetPasteInsertionIndex()
        {
            var selectedBlocks = vm.Stack?.Where(b => b != null && b.Selected).ToList() ?? new List<BlockViewModel>();
            var insertIndex = 0;

            if (selectedBlocks.Any())
            {
                var lastSelectedIndex = vm.Stack.ToList().FindLastIndex(b => selectedBlocks.Contains(b));
                insertIndex = lastSelectedIndex + 1;
            }
            else
            {
                insertIndex = vm.Stack?.Count ?? 0;
            }
            return insertIndex;
        }

        private void PerformBlockPasting(List<BlockInstance> blocksToPaste, int insertIndex, List<(int index, BlockViewModel blockVm)> undoInfo)
        {
            var pastedBlocks = new List<BlockViewModel>();

            foreach (var blockToPaste in blocksToPaste)
            {
                var newBlockVm = new BlockViewModel(blockToPaste);

                if (insertIndex >= 0 && insertIndex <= (vm.Stack?.Count ?? 0))
                {
                    vm.Stack?.Insert(insertIndex, newBlockVm);
                    pastedBlocks.Add(newBlockVm);
                    undoInfo.Add((insertIndex, newBlockVm));
                    insertIndex++;
                }
            }

            if (pastedBlocks.Any())
            {
                RecordPasteForUndo(undoInfo);

                foreach (var pastedBlock in pastedBlocks)
                {
                    pastedBlock.Selected = true;
                }

                if (vm.Stack != null)
                {
                    configService.SelectedConfig.Stack = vm.Stack
                        .Where(b => b != null && b.Block != null)
                        .Select(b => b.Block)
                        .ToList();
                }
            }
        }

        // Store paste operations for undo functionality
        private List<(int index, BlockViewModel blockVm)> lastPasteOperation = new List<(int, BlockViewModel)>();

        private void RecordPasteForUndo(List<(int index, BlockViewModel blockVm)> pasteInfo)
        {
            ClearCloneUndo(); // Clear clone undo when doing paste operation
            lastPasteOperation = pasteInfo;
        }

        // Enhanced undo that handles delete, paste, and clone operations
        private void UndoLastOperation()
        {
            // Try to undo clone operations first (most recent)
            if (lastCloneOperation.Any())
            {
                // Remove cloned blocks in reverse order to maintain indices
                foreach (var (index, blockVm) in lastCloneOperation.OrderByDescending(x => x.index))
                {
                    if (vm.Stack != null && index >= 0 && index < vm.Stack.Count && vm.Stack[index] == blockVm)
                    {
                        vm.Stack.RemoveAt(index);
                    }
                }

                // Update the config stack
                if (vm.Stack != null)
                {
                    configService.SelectedConfig.Stack = vm.Stack
                        .Where(b => b != null && b.Block != null)
                        .Select(b => b.Block)
                        .ToList();
                }

                // Clear the clone operation record
                lastCloneOperation.Clear();

                ShowAutoNotification("Undo", "Clone operation undone");
                return;
            }

            // Then try to undo paste operations
            if (lastPasteOperation.Any())
            {
                // Remove pasted blocks in reverse order to maintain indices
                foreach (var (index, blockVm) in lastPasteOperation.OrderByDescending(x => x.index))
                {
                    if (vm.Stack != null && index >= 0 && index < vm.Stack.Count && vm.Stack[index] == blockVm)
                    {
                        vm.Stack.RemoveAt(index);
                    }
                }

                // Update the config stack
                if (vm.Stack != null)
                {
                    configService.SelectedConfig.Stack = vm.Stack
                        .Where(b => b != null && b.Block != null)
                        .Select(b => b.Block)
                        .ToList();
                }

                // Clear the paste operation record
                lastPasteOperation.Clear();

                ShowAutoNotification("Undo", "Paste operation undone");
                return;
            }

            // Finally, fall back to the standard undo (delete operations)
            vm.Undo(); // This method handles its own empty case silently
        }

        private static List<BlockInstance> ParseBlocksFromText(string text)
        {
            var blocks = new List<BlockInstance>();

            if (string.IsNullOrWhiteSpace(text))
                return blocks;

            // Split by double newlines to get individual block definitions
            var blockTexts = text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var blockText in blockTexts)
            {
                var block = TryCreateBlockFromDetailedText(blockText);
                if (block != null)
                {
                    blocks.Add(block);
                }
            }

            // If no detailed blocks found, try simple line-by-line parsing
            if (!blocks.Any())
            {
                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine))
                        continue;

                    var block = TryCreateBlockFromText(trimmedLine);
                    if (block != null)
                    {
                        blocks.Add(block);
                    }
                }
            }

            return blocks;
        }

        private static BlockInstance TryCreateBlockFromDetailedText(string text)
        {
            try
            {
                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (!lines.Any())
                    return null;

                var firstLine = lines[0].Trim();
                if (firstLine.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase))
                {
                    return ParseBlockFromBlockIdFormat(lines);
                }

                return ParseBlockFromFallbackFormat(lines);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create block from detailed text: {ex.Message}");
                return null;
            }
        }

        private static BlockInstance ParseBlockFromBlockIdFormat(string[] lines)
        {
            var firstLine = lines[0].Trim();
            var blockId = firstLine.Substring(6).Trim();

            var blockContent = new List<string>();
            var foundEndBlock = false;

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line == "ENDBLOCK")
                {
                    foundEndBlock = true;
                    break;
                }
                blockContent.Add(lines[i]);
            }

            if (foundEndBlock)
            {
                try
                {
                    var block = BlockFactory.GetBlock<BlockInstance>(blockId);
                    var contentScript = string.Join(Environment.NewLine, blockContent);
                    var lineNumber = 0;
                    block.FromLC(ref contentScript, ref lineNumber);
                    return block;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to create block {blockId}: {ex.Message}");
                    return null;
                }
            }
            return null;
        }

        private static BlockInstance ParseBlockFromFallbackFormat(string[] lines)
        {
            string blockType = null;
            string label = null;
            bool disabled = false;
            var settings = new Dictionary<string, string>();

            foreach (var line in lines)
            {
                ParseFallbackLine(line, ref blockType, ref label, ref disabled, settings);
            }

            if (string.IsNullOrEmpty(blockType))
                return null;

            return CreateAndPopulateFallbackBlock(blockType, label, disabled, settings);
        }

        private static void ParseFallbackLine(
            string line,
            ref string blockType,
            ref string label,
            ref bool disabled,
            Dictionary<string, string> settings)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase))
                blockType = trimmedLine.Substring(6).Trim();
            else if (trimmedLine.StartsWith("LABEL:", StringComparison.OrdinalIgnoreCase))
                label = trimmedLine.Substring(6).Trim();
            else if (trimmedLine.StartsWith("DISABLED:", StringComparison.OrdinalIgnoreCase))
                disabled = trimmedLine.Substring(9).Trim().ToLower() == "true";
            else if (trimmedLine.Contains(':') && !trimmedLine.StartsWith("  ", StringComparison.OrdinalIgnoreCase))
            {
                TryParseSetting(trimmedLine, settings);
            }
        }

        private static BlockInstance CreateAndPopulateFallbackBlock(
            string blockType,
            string label,
            bool disabled,
            Dictionary<string, string> settings)
        {
            BlockInstance fallbackBlock = CreateFallbackBlock(blockType);

            if (fallbackBlock != null)
            {
                if (!string.IsNullOrEmpty(label))
                    fallbackBlock.Label = label;
                fallbackBlock.Disabled = disabled;
                ApplySettingsToBlock(fallbackBlock, settings);
            }
            return fallbackBlock;
        }

        private static void TryParseSetting(string trimmedLine, Dictionary<string, string> settings)
        {
            var colonIndex = trimmedLine.IndexOf(':');
            var key = trimmedLine.Substring(0, colonIndex).Trim();
            var value = trimmedLine.Substring(colonIndex + 1).Trim();
            settings[key] = value;
        }

        private static BlockInstance CreateFallbackBlock(string blockType)
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
            if (block?.Settings == null) return;

            foreach (var setting in settings)
            {
                var key = setting.Key.ToLower();
                var value = setting.Value;

                // Try to find matching setting in block
                if (block.Settings.TryGetValue(key, out var blockSetting))
                {
                    try
                    {
                        HandleSettingValue(blockSetting, value);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to set setting {key}: {ex.Message}");
                    }
                }
            }
        }

        private static void HandleSettingValue(BlockSetting blockSetting, string value)
        {
            if (value.StartsWith('@'))
            {
                blockSetting.InputMode = RuriLib.Models.Blocks.Settings.SettingInputMode.Variable;
                blockSetting.InputVariableName = value.Substring(1);
            }
            else
            {
                SetFixedSettingValue(blockSetting, value);
            }
        }

        private static void SetFixedSettingValue(BlockSetting blockSetting, string value)
        {
            blockSetting.InputMode = RuriLib.Models.Blocks.Settings.SettingInputMode.Fixed;

            if (blockSetting.FixedSetting is RuriLib.Models.Blocks.Settings.StringSetting stringSetting)
            {
                SetStringSettingValue(stringSetting, value);
            }
            else if (blockSetting.FixedSetting is RuriLib.Models.Blocks.Settings.IntSetting intSetting)
            {
                SetIntSettingValue(intSetting, value);
            }
            else if (blockSetting.FixedSetting is RuriLib.Models.Blocks.Settings.BoolSetting boolSetting)
            {
                SetBoolSettingValue(boolSetting, value);
            }
            else
            {
                blockSetting.InputMode = RuriLib.Models.Blocks.Settings.SettingInputMode.Interpolated;
                blockSetting.InterpolatedSetting = new InterpolatedStringSetting { Value = value };
            }
        }

        private static void SetStringSettingValue(RuriLib.Models.Blocks.Settings.StringSetting setting, string value)
        {
            setting.Value = value;
        }

        private static void SetIntSettingValue(RuriLib.Models.Blocks.Settings.IntSetting setting, string value)
        {
            if (int.TryParse(value, out var intValue))
                setting.Value = intValue;
        }

        private static void SetBoolSettingValue(RuriLib.Models.Blocks.Settings.BoolSetting setting, string value)
        {
            if (bool.TryParse(value, out var boolValue))
                setting.Value = boolValue;
        }

        private static BlockInstance TryCreateBlockFromText(string text)
        {
            try
            {
                return GetBlockInstanceFromText(text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create block from text: {ex.Message}");
                return null;
            }
        }

        private static BlockInstance GetBlockInstanceFromText(string text)
        {
            // Simple heuristics to create blocks from text
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
                // Default to LoliCode block for any other text
                var loliCodeBlock = BlockFactory.GetBlock<LoliCodeBlockInstance>("LoliCode");
                loliCodeBlock.Script = text;
                block = loliCodeBlock;
            }

            SetBlockLabelFromText(block, text);
            return block;
        }

        private static void SetBlockLabelFromText(BlockInstance block, string text)
        {
            block.Label = text.Length > 50 ? text.Substring(0, 50) + "..." : text;
        }

        private static void ShowAutoNotification(string title, string message)
        {
            try
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var notification = new SharedNotificationWindow(title, message);

                    // Prevent the notification from stealing focus
                    notification.ShowActivated = false;
                    notification.Focusable = false;

                    notification.Show();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show notification: {ex.Message}");
                // Fallback to simple debug output
                System.Diagnostics.Debug.WriteLine($"{title}: {message}");
            }
        }

        private void ClearPasteUndo()
        {
            if (lastPasteOperation.Any())
            {
                System.Diagnostics.Debug.WriteLine($"Clearing paste undo - had {lastPasteOperation.Count} operations");
            }
            lastPasteOperation.Clear();
        }

        private void ClearCloneUndo()
        {
            if (lastCloneOperation.Any())
            {
                System.Diagnostics.Debug.WriteLine($"Clearing clone undo - had {lastCloneOperation.Count} operations");
            }
            lastCloneOperation.Clear();
        }

        private void ClearAllUndo()
        {
            ClearPasteUndo();
            ClearCloneUndo();
        }
    }
}

