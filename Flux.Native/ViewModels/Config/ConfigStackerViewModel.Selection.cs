using System;
using System.Linq;

namespace Flux.Native.ViewModels.Configs;

public partial class ConfigStackerViewModel
{
    public void SelectBlock(BlockViewModel block, bool ctrl = false, bool shift = false)
    {
        if (Stack != null && LastSelectedBlock != null && !Stack.Contains(LastSelectedBlock))
        {
            LastSelectedBlock = null;
        }

        if (ctrl)
        {
            HandleCtrlSelection(block);
        }
        else if (shift)
        {
            HandleShiftSelection(block);
        }
        else
        {
            HandleNormalSelection(block);
        }

        InvokeSelectionChanged();
    }

    private void HandleCtrlSelection(BlockViewModel block)
    {
        if (block != null)
        {
            block.Selected = !block.Selected;
            LastSelectedBlock = block.Selected ? block : null;
        }
    }

    private void HandleShiftSelection(BlockViewModel block)
    {
        if (LastSelectedBlock == null || LastSelectedBlock == block)
        {
            if (block != null)
            {
                block.Selected = true;
            }

            LastSelectedBlock = block;
        }
        else
        {
            UnselectAllBlocks();
            SelectBlockRange(block);
            LastSelectedBlock = block;
        }
    }

    private void UnselectAllBlocks()
    {
        if (Stack != null)
        {
            foreach (var b in Stack.Where(static b => b != null))
            {
                b.Selected = false;
            }
        }
    }

    private void SelectBlockRange(BlockViewModel block)
    {
        if (Stack != null && LastSelectedBlock != null && block != null)
        {
            var lastSelectedBlockIndex = Stack.IndexOf(LastSelectedBlock);
            var itemIndex = Stack.IndexOf(block);

            if (lastSelectedBlockIndex != -1 && itemIndex != -1)
            {
                var minIndex = Math.Min(lastSelectedBlockIndex, itemIndex);
                var maxIndex = Math.Max(lastSelectedBlockIndex, itemIndex);

                SetSelectedStateForRange(minIndex, maxIndex, true);
            }
        }
    }

    private void SetSelectedStateForRange(int minIndex, int maxIndex, bool selectedState)
    {
        for (var i = minIndex; i <= maxIndex; i++)
        {
            if (i >= 0 && i < Stack.Count)
            {
                var item = Stack[i];
                if (item != null)
                {
                    item.Selected = selectedState;
                }
            }
        }
    }

    private void HandleNormalSelection(BlockViewModel block)
    {
        if (Stack != null)
        {
            foreach (var b in Stack.Where(static b => b != null))
            {
                b.Selected = false;
            }
        }

        if (block != null)
        {
            block.Selected = true;
        }

        LastSelectedBlock = block?.Selected == true ? block : null;
    }

    private void InvokeSelectionChanged()
    {
        var selected = Stack != null
            ? Stack.Where(static s => s?.Selected == true).ToArray()
            : Array.Empty<BlockViewModel>();

        Inspector.SetSelectedBlock(selected.FirstOrDefault());
        SelectionChanged?.Invoke(selected);
        RaiseToolCommandStateChanged();
    }
}
