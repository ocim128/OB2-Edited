using Flux.Core.Services;
using RuriLib.Helpers;
using RuriLib.Helpers.Blocks;
using RuriLib.Models.Blocks;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Flux.Native.ViewModels.Base;


namespace Flux.Native.ViewModels.Configs;

    public partial class ConfigStackerViewModel : ViewModelBase
{
    private readonly ConfigService configService;
    private readonly ConfigStackerState state = new();

    public event Action<IEnumerable<BlockViewModel>> SelectionChanged;

    private ObservableCollection<BlockViewModel> stack;

    public ConfigStackerInspectorViewModel Inspector { get; }

    private List<BlockViewModel> OriginalStack
    {
        get => state.OriginalStack;
        set => state.OriginalStack = value ?? [];
    }

    private BlockViewModel? LastSelectedBlock
    {
        get => state.LastSelectedBlock;
        set => state.LastSelectedBlock = value;
    }

    public ObservableCollection<BlockViewModel> Stack
    {
        get => stack;
        set
        {
            stack = value;
            OnPropertyChanged();
        }
    }

    public ConfigStackerViewModel(ConfigService configService)
    {
        this.configService = configService ?? throw new ArgumentNullException(nameof(configService));
        Inspector = new ConfigStackerInspectorViewModel();
        InitializeTooling();
    }

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
            OriginalStack = new List<BlockViewModel>(Stack.Where(static b => b != null));
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
            OriginalStack = new List<BlockViewModel>(Stack.Where(static b => b != null));
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
        if (Stack != null)
        {
            OriginalStack = new List<BlockViewModel>(Stack.Where(static b => b != null));

            configService.SelectedConfig.Stack = Stack
                .Where(static b => b?.Block != null)
                .Select(static b => b.Block)
                .ToList();
        }
        else
        {
            configService.SelectedConfig.Stack = [];
            OriginalStack = [];
        }
    }

    public void ApplySearchFilter(string searchText)
    {
        Stack ??= [];

        if (OriginalStack.Count == 0 && Stack.Count > 0)
        {
            OriginalStack = new List<BlockViewModel>(Stack.Where(static b => b != null));
        }

        Stack.Clear();

        if (string.IsNullOrWhiteSpace(searchText))
        {
            AddBlocksToStack(OriginalStack);
        }
        else
        {
            FilterAndAddBlocksToStack(searchText);
        }
    }

    private void AddBlocksToStack(IEnumerable<BlockViewModel> blocks)
    {
        if (blocks != null)
        {
            foreach (var block in blocks.Where(static b => b != null))
            {
                Stack.Add(block);
            }
        }
    }

    private void FilterAndAddBlocksToStack(string searchText)
    {
        var lowerSearchText = searchText.ToLowerInvariant();

        if (OriginalStack.Count > 0)
        {
            foreach (var block in OriginalStack.Where(static b => b != null))
            {
                var matchesLabel = block.Label.ToLowerInvariant().Contains(lowerSearchText);
                var matchesType = block.Block?.Descriptor?.Name != null && block.Block.Descriptor.Name.ToLowerInvariant().Contains(lowerSearchText);

                if (matchesLabel || matchesType)
                {
                    Stack.Add(block);
                }
            }
        }
    }
}

public class BlockViewModel(BlockInstance block) : ViewModelBase
{
    public BlockInstance Block { get; init; } = block;

    private bool selected = false;
    public bool Selected
    {
        get => selected;
        set
        {
            selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundColor));
            OnPropertyChanged(nameof(ForegroundColor));
        }
    }

    public string Label
    {
        get => Block?.Label ?? string.Empty;

        set
        {
            if (Block != null)
            {
                Block.Label = value;
                OnPropertyChanged();
            }
        }
    }

    public bool Disabled
    {
        get => Block?.Disabled ?? false;
        set
        {
            if (Block != null)
            {
                Block.Disabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BackgroundColor));
                OnPropertyChanged(nameof(ForegroundColor));
            }
        }
    }

    public string BackgroundColor
    {
        get
        {
            if (Disabled)
            {
                return "#BDBDBD"; // Medium gray for disabled blocks
            }
            else if (Selected)
            {
                return "#BBDEFB"; // Light blue for selected blocks
            }
            else
            {
                return (Block?.Descriptor?.Category != null) ? Block.Descriptor.Category.BackgroundColor : "#E3F2FD"; // Light blue default
            }
        }
    }

    public string ForegroundColor
    {
        get
        {
            if (Selected)
            {
                return "#0D47A1"; // Dark blue for selected blocks
            }
            else if (Disabled)
            {
                return "#FFFFFF"; // White for disabled blocks
            }
            else
            {
                return (Block?.Descriptor?.Category != null) ? Block.Descriptor.Category.ForegroundColor : "#0D47A1"; // Dark blue default
            }
        }
    }
}


