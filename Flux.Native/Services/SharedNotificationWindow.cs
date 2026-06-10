using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Flux.Native.Services;

/// <summary>
/// Toast notification window using App.xaml styles.
/// Auto-closes after 4 seconds with fade animation.
/// </summary>
public class SharedNotificationWindow : System.Windows.Window
{
    public SharedNotificationWindow(string title, string message)
    {
        Width = 320;
        Height = 90;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 30;
        Top = workArea.Bottom - Height - 50;

        var mainBorder = new Border();
        mainBorder.SetResourceReference(StyleProperty, "ModernNotificationWindow");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconPath = new Path();
        iconPath.SetResourceReference(StyleProperty, "NotificationIconStyle");
        iconPath.Data = Geometry.Parse(GetIconPath(title));
        iconPath.Fill = GetIconColor(title);
        Grid.SetColumn(iconPath, 0);
        grid.Children.Add(iconPath);

        var textPanel = new StackPanel
        {
            Margin = new Thickness(5, 10, 15, 10),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(textPanel, 1);

        var titleBlock = new TextBlock { Text = title };
        titleBlock.SetResourceReference(StyleProperty, "NotificationTitleStyle");

        var messageBlock = new TextBlock { Text = message };
        messageBlock.SetResourceReference(StyleProperty, "NotificationMessageStyle");

        textPanel.Children.Add(titleBlock);
        textPanel.Children.Add(messageBlock);
        grid.Children.Add(textPanel);

        mainBorder.Child = grid;
        Content = mainBorder;

        // Fade in
        var fadeIn = new DoubleAnimation
        {
            From = 0, To = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new QuadraticEase()
        };
        BeginAnimation(OpacityProperty, fadeIn);

        // Auto-close after 4 seconds
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            var fadeOut = new DoubleAnimation
            {
                From = 1, To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase()
            };
            fadeOut.Completed += (s2, e2) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        };
        timer.Start();

        MouseDown += (s, e) => { timer.Stop(); Close(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HideFromAltTab();
    }

    private void HideFromAltTab()
    {
        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        if (handle == IntPtr.Zero) return;

        var styles = GetExtendedStyles(handle).ToInt64();
        styles |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        styles &= ~WS_EX_APPWINDOW;
        SetExtendedStyles(handle, new IntPtr(styles));
    }

    private static IntPtr GetExtendedStyles(IntPtr handle)
        => IntPtr.Size == 8
            ? GetWindowLongPtr64(handle, GWL_EXSTYLE)
            : new IntPtr(GetWindowLong(handle, GWL_EXSTYLE));

    private static void SetExtendedStyles(IntPtr handle, IntPtr styles)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(handle, GWL_EXSTYLE, styles);
        else
            SetWindowLong(handle, GWL_EXSTYLE, styles.ToInt32());
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static string GetIconPath(string title) => title.ToLower() switch
    {
        var t when t.Contains("error") => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z",
        var t when t.Contains("enabled") || t.Contains("success") => "M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z",
        var t when t.Contains("disabled") => "M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z",
        var t when t.Contains("text sent") => "M2.01 21L23 12 2.01 3 2 10l15 2-15 2z",
        _ => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"
    };

    private static readonly SolidColorBrush ErrorBrush = FreezeBrush(Color.FromRgb(239, 68, 68));
    private static readonly SolidColorBrush SuccessBrush = FreezeBrush(Color.FromRgb(34, 197, 94));
    private static readonly SolidColorBrush DisabledBrush = FreezeBrush(Color.FromRgb(156, 163, 175));
    private static readonly SolidColorBrush SentBrush = FreezeBrush(Color.FromRgb(59, 130, 246));
    private static readonly SolidColorBrush DefaultBrush = FreezeBrush(Color.FromRgb(129, 140, 248));

    private static SolidColorBrush FreezeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Brush GetIconColor(string title) => title.ToLower() switch
    {
        var t when t.Contains("error") => ErrorBrush,
        var t when t.Contains("enabled") || t.Contains("success") => SuccessBrush,
        var t when t.Contains("disabled") => DisabledBrush,
        var t when t.Contains("text sent") => SentBrush,
        _ => DefaultBrush
    };
}
