using Flux.Native.ViewModels.Base;
using System;

namespace Flux.Native.ViewModels.Configs;

internal sealed class ConfigStackerCommandSet
{
    public RelayCommand RemoveSelectedCommand { get; }

    public RelayCommand MoveSelectedUpCommand { get; }

    public RelayCommand MoveSelectedDownCommand { get; }

    public RelayCommand CloneSelectedCommand { get; }

    public RelayCommand ToggleDisabledCommand { get; }

    public RelayCommand UndoCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand PasteCommand { get; }

    public ConfigStackerCommandSet(
        Action removeSelected,
        Action moveSelectedUp,
        Action moveSelectedDown,
        Action cloneSelected,
        Action toggleDisabled,
        Action undo,
        Action copy,
        Action paste,
        Func<bool> hasSelectedBlocks,
        Func<bool> canUndo)
    {
        RemoveSelectedCommand = new RelayCommand(removeSelected, hasSelectedBlocks);
        MoveSelectedUpCommand = new RelayCommand(moveSelectedUp, hasSelectedBlocks);
        MoveSelectedDownCommand = new RelayCommand(moveSelectedDown, hasSelectedBlocks);
        CloneSelectedCommand = new RelayCommand(cloneSelected, hasSelectedBlocks);
        ToggleDisabledCommand = new RelayCommand(toggleDisabled, hasSelectedBlocks);
        UndoCommand = new RelayCommand(undo, canUndo);
        CopyCommand = new RelayCommand(copy, hasSelectedBlocks);
        PasteCommand = new RelayCommand(paste);
    }

    public void RaiseCanExecuteChanged()
    {
        RemoveSelectedCommand.RaiseCanExecuteChanged();
        MoveSelectedUpCommand.RaiseCanExecuteChanged();
        MoveSelectedDownCommand.RaiseCanExecuteChanged();
        CloneSelectedCommand.RaiseCanExecuteChanged();
        ToggleDisabledCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
        CopyCommand.RaiseCanExecuteChanged();
        PasteCommand.RaiseCanExecuteChanged();
    }
}
