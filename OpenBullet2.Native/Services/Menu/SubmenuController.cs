using System;
using System.Windows;

namespace OpenBullet2.Native.Services.Menu;

public class SubmenuController
{
    private FrameworkElement _submenu;
    private FrameworkElement _chevron;
    private bool _isOpen;

    public event EventHandler<bool> SubmenuStateChanged;

    public void Initialize(FrameworkElement submenu, FrameworkElement chevron)
    {
        _submenu = submenu;
        _chevron = chevron;
    }

    public bool IsOpen => _isOpen;

    public void Open()
    {
        _isOpen = true;
        if (_submenu != null) _submenu.Visibility = Visibility.Visible;
        if (_chevron != null) _chevron.Visibility = Visibility.Visible;
        SubmenuStateChanged?.Invoke(this, true);
    }

    public void Close()
    {
        _isOpen = false;
        if (_submenu != null) _submenu.Visibility = Visibility.Collapsed;
        if (_chevron != null) _chevron.Visibility = Visibility.Collapsed;
        SubmenuStateChanged?.Invoke(this, false);
    }

    public void Toggle()
    {
        if (_isOpen)
            Close();
        else
            Open();
    }
}
