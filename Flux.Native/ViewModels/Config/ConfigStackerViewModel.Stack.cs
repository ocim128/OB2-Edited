using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RuriLib.Helpers;
using RuriLib.Helpers.Blocks;
using RuriLib.Models.Blocks;

namespace Flux.Native.ViewModels.Configs;

public partial class ConfigStackerViewModel
{
    public void CreateBlock(BlockDescriptor descriptor)
    {
        var newBlockInstance = BlockFactory.GetBlock<BlockInstance>(descriptor.Id);
        var newBlockVm = new BlockViewModel(newBlockInstance);

        var insertIndex = GetInsertIndex();

        Stack.Insert(insertIndex, newBlockVm);

        if (OriginalStack.Count > 0)
        {
            if (insertIndex >= 0 && insertIndex <= OriginalStack.Count)
            {
                OriginalStack.Insert(insertIndex, newBlockVm);
            }
            else
            {
                OriginalStack.Add(newBlockVm);
            }
        }

        SelectBlock(newBlockVm, false);
        SaveStack();

        ApplySearchFilter(null);
    }

    private int GetInsertIndex()
    {
        var insertIndex = Stack.Count;
        if (Stack.Any(static b => b?.Selected == true))
        {
            for (var i = Stack.Count - 1; i >= 0; i--)
            {
                if (Stack[i]?.Selected == true)
                {
                    insertIndex = i + 1;
                    break;
                }
            }
        }

        if (insertIndex < 0 || insertIndex > Stack.Count)
        {
            insertIndex = Stack.Count;
        }

        return insertIndex;
    }

    public void RemoveSelected()
    {
        if (Stack == null)
        {
            return;
        }

        var selectedBlocksToRemove = Stack.Where(static b => b?.Selected == true).ToList();

        for (var i = Stack.Count - 1; i >= 0; i--)
        {
            if (i >= 0 && i < Stack.Count)
            {
                var block = Stack[i];

                if (block != null && selectedBlocksToRemove.Contains(block))
                {
                    state.DeletedBlocks.Add((block.Block, i));
                    Stack.RemoveAt(i);
                }
            }
        }

        RemoveBlocksFromOriginalStack(selectedBlocksToRemove);

        SelectBlock(null, false);
        SaveStack();
        ApplySearchFilter(null);
    }

    private void RemoveBlocksFromOriginalStack(List<BlockViewModel> selectedBlocksToRemove)
    {
        if (OriginalStack.Count > 0)
        {
            for (var i = OriginalStack.Count - 1; i >= 0; i--)
            {
                if (i >= 0 && i < OriginalStack.Count)
                {
                    var originalBlockVm = OriginalStack[i];

                    if (originalBlockVm != null && selectedBlocksToRemove.Contains(originalBlockVm))
                    {
                        OriginalStack.RemoveAt(i);
                    }
                }
            }
        }
    }

    public void MoveSelectedUp()
    {
        if (Stack == null)
        {
            return;
        }

        for (var i = 0; i < Stack.Count; i++)
        {
            if (i >= 0 && i < Stack.Count)
            {
                var block = Stack[i];

                if (block?.Selected == true && i > 0)
                {
                    Stack.Move(i, i - 1);
                }
            }
        }

        if (Stack != null)
        {
            SyncOriginalStackFromStackIfUnfiltered();
        }

        SaveStack();
    }

    public void MoveSelectedDown()
    {
        if (Stack == null)
        {
            return;
        }

        for (var i = Stack.Count - 1; i >= 0; i--)
        {
            if (i >= 0 && i < Stack.Count)
            {
                var block = Stack[i];

                if (block?.Selected == true && i < Stack.Count - 1)
                {
                    Stack.Move(i, i + 1);
                }
            }
        }

        if (Stack != null)
        {
            SyncOriginalStackFromStackIfUnfiltered();
        }

        SaveStack();
    }

    public void CloneSelected()
    {
        if (Stack == null)
        {
            return;
        }

        foreach (var blockVm in Stack.Where(static b => b?.Selected == true).ToList())
        {
            CloneAndInsertBlock(blockVm);
        }

        SaveStack();
        ApplySearchFilter(null);
    }

    private void CloneAndInsertBlock(BlockViewModel blockVm)
    {
        if (blockVm?.Block != null)
        {
            var newBlockInstance = Cloner.Clone(blockVm.Block);
            var newBlockVm = new BlockViewModel(newBlockInstance);

            var insertIndex = Stack.IndexOf(blockVm) + 1;

            if (insertIndex >= 0 && insertIndex <= Stack.Count)
            {
                Stack.Insert(insertIndex, newBlockVm);
                InsertIntoOriginalStackForClone(newBlockVm, blockVm);
            }
            else
            {
                Stack.Add(newBlockVm);
                OriginalStack.Add(newBlockVm);
            }
        }
        else
        {
            Console.WriteLine("Attempted to clone a null BlockViewModel or one with a null BlockInstance!");
        }
    }

    private void InsertIntoOriginalStackForClone(BlockViewModel newBlockVm, BlockViewModel originalBlockVm)
    {
        if (OriginalStack.Count > 0)
        {
            var originalIndex = OriginalStack.FindIndex(b => b != null && b.Block == originalBlockVm.Block);

            if (originalIndex != -1)
            {
                var originalInsertIndex = originalIndex + 1;
                if (originalInsertIndex >= 0 && originalInsertIndex <= OriginalStack.Count)
                {
                    OriginalStack.Insert(originalInsertIndex, newBlockVm);
                }
                else
                {
                    OriginalStack.Add(newBlockVm);
                }
            }
            else
            {
                OriginalStack.Add(newBlockVm);
            }
        }
    }

    public void EnableDisableSelected()
    {
        if (Stack == null)
        {
            return;
        }

        foreach (var blockVm in Stack.Where(b => b?.Selected == true))
        {
            if (blockVm?.Block != null)
            {
                blockVm.Block.Disabled = !blockVm.Block.Disabled;
                blockVm.Disabled = blockVm.Block.Disabled;

                if (OriginalStack.Count > 0)
                {
                    var originalBlockVm = OriginalStack.FirstOrDefault(b => b != null && b.Block == blockVm.Block);
                    if (originalBlockVm?.Block != null)
                    {
                        originalBlockVm.Block.Disabled = blockVm.Block.Disabled;
                        originalBlockVm.Disabled = blockVm.Block.Disabled;
                    }
                }
            }
            else
            {
                Console.WriteLine("Attempted to enable/disable a null BlockViewModel or one with a null BlockInstance!");
            }
        }
    }

    public void Undo()
    {
        if (state.DeletedBlocks.Count == 0)
        {
            return;
        }

        var toRestore = state.DeletedBlocks[^1];
        _ = state.DeletedBlocks.Remove(toRestore);

        if (toRestore.Item1 != null)
        {
            var restoredBlockVm = new BlockViewModel(toRestore.Item1);

            InsertIntoStack(restoredBlockVm, toRestore.Index);
            InsertIntoOriginalStack(restoredBlockVm, toRestore.Index);
        }
        else
        {
            Console.WriteLine("Attempted to undo a deleted block with a null BlockInstance!");
        }

        SaveStack();
        ApplySearchFilter(null);
    }

    private void InsertIntoStack(BlockViewModel blockVm, int index)
    {
        if (Stack != null && index >= 0 && index <= Stack.Count)
        {
            Stack.Insert(index, blockVm);
        }
        else if (Stack != null)
        {
            Stack.Add(blockVm);
        }
        else
        {
            Stack = [blockVm];
        }
    }

    private void InsertIntoOriginalStack(BlockViewModel blockVm, int index)
    {
        if (index >= 0 && index <= OriginalStack.Count)
        {
            OriginalStack.Insert(index, blockVm);
        }
        else
        {
            OriginalStack.Add(blockVm);
        }
    }

    public override void UpdateViewModel()
    {
        if (configService?.SelectedConfig?.Stack == null)
        {
            OriginalStack = [];
            Stack = [];
            Inspector.SetSelectedBlock(null);
            RaiseToolCommandStateChanged();
            base.UpdateViewModel();
            return;
        }

        OriginalStack = configService.SelectedConfig.Stack
            .Where(static b => b != null)
            .Select(static b => new BlockViewModel(b))
            .ToList();

        Stack = new ObservableCollection<BlockViewModel>(OriginalStack.Where(static b => b != null));

        if (Stack.Count == 0)
        {
            SelectBlock(null, false);
        }

        RaiseToolCommandStateChanged();
        base.UpdateViewModel();
    }

    private void SaveStack()
    {
        // Always save from OriginalStack (full unfiltered list), never from
        // the potentially-filtered Stack view. Some callers first sync
        // OriginalStack from Stack (e.g. move operations when no filter is
        // active), which is fine — those callers know the filter state.
        configService.SelectedConfig.Stack = OriginalStack
            .Where(static b => b?.Block != null)
            .Select(static b => b.Block)
            .ToList();
    }

    private void SyncOriginalStackFromStackIfUnfiltered()
    {
        // Only rebuild OriginalStack from the visual Stack when no search
        // filter is active. During filtering, Stack is a subset and we must
        // preserve the full OriginalStack.
        if (string.IsNullOrWhiteSpace(state.SearchText))
        {
            OriginalStack = new List<BlockViewModel>(Stack.Where(static b => b != null));
        }
        else
        {
            // Reorder OriginalStack to match the order of Stack items,
            // preserving non-matching items at the end.
            var orderedFromFilter = Stack.Where(static b => b != null).ToList();
            var remaining = OriginalStack
                .Where(b => b != null && !orderedFromFilter.Contains(b))
                .ToList();
            OriginalStack = [.. orderedFromFilter, .. remaining];
        }
    }
}
