using System;
using System.Collections.Generic;
using System.Linq;

namespace Flux.Native.ViewModels.Configs;

public partial class ConfigStackerViewModel
{
    public string SearchText
    {
        get => state.SearchText;
        set
        {
            var nextSearchText = value ?? string.Empty;
            if (!string.Equals(state.SearchText, nextSearchText, StringComparison.Ordinal))
            {
                state.SearchText = nextSearchText;
                OnPropertyChanged();
                ScheduleSearchApply();
            }
        }
    }

    private void ScheduleSearchApply()
    {
        searchDebouncer.Schedule(300, () =>
        {
            ApplySearchFilter(state.SearchText);
            RaiseToolCommandStateChanged();
        });
    }

    private void ClearSearch()
    {
        searchDebouncer.Cancel();
        state.SearchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        ApplySearchFilter(string.Empty);
    }

    public void ApplySearchFilter(string searchText)
    {
        Stack ??= [];

        if (OriginalStack.Count == 0 && Stack.Count > 0)
        {
            OriginalStack = new List<BlockViewModel>(Stack.Where(static b => b != null));
        }

        // Clear selection on blocks that will be filtered out
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var lowerSearchText = searchText.ToLowerInvariant();
            foreach (var block in Stack.Where(static b => b != null))
            {
                var matchesLabel = block.Label.ToLowerInvariant().Contains(lowerSearchText);
                var matchesType = block.Block?.Descriptor?.Name != null
                    && block.Block.Descriptor.Name.ToLowerInvariant().Contains(lowerSearchText);
                if (!matchesLabel && !matchesType && block.Selected)
                {
                    block.Selected = false;
                }
            }
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
