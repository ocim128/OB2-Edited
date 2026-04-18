using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Flux.Core.Services;
using Flux.Native.ViewModels.Base;

namespace Flux.Native.ViewModels.Configs;

public partial class ConfigStackerViewModel : ViewModelBase, IDisposable
{
    private readonly ConfigService configService;
    private readonly ConfigStackerState state = new();

    public event Action<IEnumerable<BlockViewModel>> SelectionChanged;

    private ObservableCollection<BlockViewModel> stack = [];

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

    public void Dispose() => searchDebouncer.Dispose();
}
