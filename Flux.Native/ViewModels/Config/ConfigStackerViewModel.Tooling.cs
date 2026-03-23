using System;
using System.Linq;
using Flux.Native.ViewModels.Base;
using RuriLib.Models.Blocks;

namespace Flux.Native.ViewModels.Configs;

public partial class ConfigStackerViewModel
{
    private readonly ConfigStackerClipboardAdapter clipboardAdapter = new();
    private readonly ConfigStackerSearchDebouncer searchDebouncer = new();
    private ConfigStackerCommandSet toolCommands = null!;

    public event Action<BlockViewModel>? SelectionBringIntoViewRequested;

    public event Action<string, string>? NotificationRequested;

    public RelayCommand RemoveSelectedCommand => toolCommands.RemoveSelectedCommand;

    public RelayCommand MoveSelectedUpCommand => toolCommands.MoveSelectedUpCommand;

    public RelayCommand MoveSelectedDownCommand => toolCommands.MoveSelectedDownCommand;

    public RelayCommand CloneSelectedCommand => toolCommands.CloneSelectedCommand;

    public RelayCommand ToggleDisabledCommand => toolCommands.ToggleDisabledCommand;

    public RelayCommand UndoCommand => toolCommands.UndoCommand;

    public RelayCommand CopyCommand => toolCommands.CopyCommand;

    public RelayCommand PasteCommand => toolCommands.PasteCommand;

    private void InitializeTooling()
    {
        toolCommands = new ConfigStackerCommandSet(
            ExecuteRemoveSelected,
            ExecuteMoveSelectedUp,
            ExecuteMoveSelectedDown,
            ExecuteCloneSelected,
            ExecuteToggleDisabled,
            UndoLastOperation,
            CopySelectedBlocks,
            PasteBlocks,
            HasSelectedBlocks,
            CanUndo);
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
        => toolCommands.RaiseCanExecuteChanged();

    private bool HasSelectedBlocks()
        => Stack?.Any(b => b is not null && b.Selected) == true;

    private bool CanUndo()
        => state.CanUndo;

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

        var selectedBlocks = Stack?.Where(b => b is not null && b.Selected).ToList() ?? [];
        if (!selectedBlocks.Any())
        {
            CloneSelected();
            return;
        }

        var originalBlocks = Stack?.ToList() ?? [];
        var originalCount = originalBlocks.Count;

        CloneSelected();

        state.LastCloneOperation.Clear();
        var newCount = Stack?.Count ?? 0;
        if (newCount > originalCount && Stack != null)
        {
            for (var i = 0; i < Stack.Count; i++)
            {
                var currentBlock = Stack[i];
                if (currentBlock is not null && !originalBlocks.Contains(currentBlock))
                {
                    state.LastCloneOperation.Add((i, currentBlock));
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

    private void ShowNotification(string title, string message)
    {
        NotificationRequested?.Invoke(title, message);
    }

    private void ClearPasteUndo()
    {
        state.ClearPasteUndo();
        RaiseToolCommandStateChanged();
    }

    private void ClearCloneUndo()
    {
        state.ClearCloneUndo();
        RaiseToolCommandStateChanged();
    }

    private void ClearAllUndo()
    {
        state.ClearAllUndo();
        RaiseToolCommandStateChanged();
    }
}
