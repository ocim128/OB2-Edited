using RuriLib.Models.Blocks;
using System.Collections.Generic;

namespace Flux.Native.ViewModels.Configs;

internal sealed class ConfigStackerState
{
    public List<(BlockInstance Block, int Index)> DeletedBlocks { get; } = [];

    public List<BlockInstance> ClipboardBlocks { get; } = [];

    public List<(int Index, BlockViewModel BlockVm)> LastCloneOperation { get; } = [];

    public List<(int Index, BlockViewModel BlockVm)> LastPasteOperation { get; } = [];

    public BlockViewModel? LastSelectedBlock { get; set; }

    public List<BlockViewModel> OriginalStack { get; set; } = [];

    public string SearchText { get; set; } = string.Empty;

    public bool CanUndo
        => LastCloneOperation.Count > 0 || LastPasteOperation.Count > 0 || DeletedBlocks.Count > 0;

    public void ClearPasteUndo() => LastPasteOperation.Clear();

    public void ClearCloneUndo() => LastCloneOperation.Clear();

    public void ClearAllUndo()
    {
        ClearPasteUndo();
        ClearCloneUndo();
    }
}
