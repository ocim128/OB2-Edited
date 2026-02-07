using MahApps.Metro.Controls;
using System;
using System.Windows;
using Flux.Native.Views.Pages;
using Flux.Native.Views.Pages.Configs;
using Flux.Core.Services;

namespace Flux.Native.Services.Window;

public class WindowControlHandler : IWindowControlHandler
{
    private MainWindow _window;
    private readonly IWindowLayoutService _windowLayoutService;

    public WindowControlHandler(IWindowLayoutService windowLayoutService)
    {
        _windowLayoutService = windowLayoutService;
    }

    public void Initialize()
    {
        // This will be called from MainWindow
    }

    public void SetWindow(MainWindow window)
    {
        _window = window;
    }

    public void Minimize() => _window.WindowState = WindowState.Minimized;

    public void MaximizeRestore()
    {
        if (_window.WindowState == WindowState.Maximized)
        {
            _window.WindowState = WindowState.Normal;
        }
        else
        {
            _window.WindowState = WindowState.Maximized;
        }
    }

    public void Close() => _window.Close();

    public void OnWindowStateChanged(object sender, EventArgs e)
    {
        NotifyDebuggerWindowStateChanged(_window.WindowState == WindowState.Minimized);
    }

    private void NotifyDebuggerWindowStateChanged(bool isMinimized)
    {
        try
        {
            if (_window.CurrentPage is ConfigEditor editor && editor.debuggerFrame?.Content is Flux.Native.Views.Pages.Shared.Debugger debugger)
            {
                debugger.SetWindowMinimized(isMinimized);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error notifying debugger of window state change: {ex.Message}");
        }
    }
}
