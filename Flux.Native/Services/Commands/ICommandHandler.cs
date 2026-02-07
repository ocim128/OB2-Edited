using System.Windows.Input;

namespace Flux.Native.Services.Commands;

public interface ICommandHandler
{
    void InitializeCommandBindings(MainWindow window);
    void OnNewConfigExecuted(object sender, ExecutedRoutedEventArgs e);
    void OnSaveConfigExecuted(object sender, ExecutedRoutedEventArgs e);
    void OnRefreshExecuted(object sender, ExecutedRoutedEventArgs e);
}
