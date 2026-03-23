using Flux.Native.ViewModels.Base;
using RuriLib.Models.Blocks;

namespace Flux.Native.ViewModels.Configs;

public class BlockViewModel(BlockInstance block) : ViewModelBase
{
    public BlockInstance Block { get; init; } = block;

    private bool selected;
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
                return "#BDBDBD";
            }

            if (Selected)
            {
                return "#BBDEFB";
            }

            return Block?.Descriptor?.Category != null ? Block.Descriptor.Category.BackgroundColor : "#E3F2FD";
        }
    }

    public string ForegroundColor
    {
        get
        {
            if (Selected)
            {
                return "#0D47A1";
            }

            if (Disabled)
            {
                return "#FFFFFF";
            }

            return Block?.Descriptor?.Category != null ? Block.Descriptor.Category.ForegroundColor : "#0D47A1";
        }
    }
}
