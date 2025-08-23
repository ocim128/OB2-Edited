using MahApps.Metro.Controls;
using System.Windows.Controls;
using System.Windows;

namespace OpenBullet2.Native;

/// <summary>
/// Interaction logic for MainDialog.xaml
/// </summary>
public partial class MainDialog : MetroWindow
{
    public MainDialog(Page content, string title, bool canResize = false)
    {
        InitializeComponent();

        // Safely set Owner to avoid null reference exception
        if (Application.Current?.MainWindow != null &&
            Application.Current.MainWindow != this &&
            Application.Current.MainWindow.IsLoaded)
        {
            Owner = Application.Current.MainWindow;
        }

        Content = content;
        Title = title;
        ResizeMode = canResize ? System.Windows.ResizeMode.CanResize : System.Windows.ResizeMode.NoResize;
        KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Close(); } };
    }

    public MainDialog(Page content, string title, int initialWidth, int initialHeight)
    {
        InitializeComponent();

        // Safely set Owner to avoid null reference exception
        if (Application.Current?.MainWindow != null &&
            Application.Current.MainWindow != this &&
            Application.Current.MainWindow.IsLoaded)
        {
            Owner = Application.Current.MainWindow;
        }

        Content = content;
        Title = title;
        ResizeMode = System.Windows.ResizeMode.CanResize;
        Width = initialWidth;
        Height = initialHeight;
        KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Close(); } };
    }
}
