using System.Windows.Input;

namespace OpenBullet2.Native.Services.Commands;

public interface ICommandHandler
{
    void InitializeCommandBindings(MainWindow window);
    void OnNewConfigExecuted(object sender, ExecutedRoutedEventArgs e);
    void OnSaveConfigExecuted(object sender, ExecutedRoutedEventArgs e);
    void OnRefreshExecuted(object sender, ExecutedRoutedEventArgs e);
}
