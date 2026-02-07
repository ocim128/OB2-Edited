using System;

namespace Flux.Native.Services.Window;

public interface IWindowControlHandler
{
    void SetWindow(MainWindow window);
    void Initialize();
    void Minimize();
    void MaximizeRestore();
    void Close();
    void OnWindowStateChanged(object sender, EventArgs e);
}
