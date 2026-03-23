using Flux.Native.ViewModels.Base;

namespace Flux.Native.ViewModels.Configs;

public sealed class ConfigStackerInspectorViewModel : ViewModelBase
{
    private BlockViewModel? selectedBlock;

    public BlockViewModel? SelectedBlock
    {
        get => selectedBlock;
        private set
        {
            if (SetProperty(ref selectedBlock, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => SelectedBlock is not null;

    public void SetSelectedBlock(BlockViewModel? block)
    {
        SelectedBlock = block;
    }
}
