using Flux.Core.Services;
using Flux.Native.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Flux.Native.Services;

public interface IWindowLayoutService
{
    void Initialize(System.Windows.Window window, FrameworkElement rootElement);
    void UpdateResponsiveLayout();
    void SaveWindowState();
    void RestoreWindowState();
}

public class WindowLayoutService : IWindowLayoutService
{
    private readonly FluxSettingsService _settingsService;
    private readonly ILogger<WindowLayoutService> _logger;
    private readonly DispatcherTimer _saveDebounceTimer;
    private System.Windows.Window _window;
    private FrameworkElement _rootElement;

    public WindowLayoutService(FluxSettingsService settingsService, ILogger<WindowLayoutService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        _saveDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _saveDebounceTimer.Tick += OnDebouncedSave;
    }

    public void Initialize(System.Windows.Window window, FrameworkElement rootElement)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _rootElement = rootElement; // specific element trigger updates if needed, logic from original code

        _window.SizeChanged += (s, e) => 
        {
            UpdateResponsiveLayout();
            ScheduleSave();
        };

        _window.LocationChanged += (s, e) => ScheduleSave();
        _window.StateChanged += (s, e) => ScheduleSave();
    }

    public void RestoreWindowState()
    {
        if (_window == null) return;

        var customization = _settingsService.Settings.CustomizationSettings;

        if (customization.RememberWindowState)
        {
            _window.Width = Math.Max(_window.MinWidth, customization.WindowWidth);
            _window.Height = Math.Max(_window.MinHeight, customization.WindowHeight);

            var workingArea = SystemParameters.WorkArea;
            _window.Left = Math.Max(0, Math.Min(customization.WindowLeft, workingArea.Right - _window.Width));
            _window.Top = Math.Max(0, Math.Min(customization.WindowTop, workingArea.Bottom - _window.Height));
            _window.WindowState = (WindowState)customization.WindowState;
        }
        else
        {
            ApplyDefaultResponsiveSizing();
        }
    }

    private void ApplyDefaultResponsiveSizing()
    {
        var workingArea = SystemParameters.WorkArea;
        var screenWidth = workingArea.Width;
        var screenHeight = workingArea.Height;

        var widthPercentage = screenWidth <= 1366 ? 0.90 : screenWidth <= 1920 ? 0.80 : 0.72;
        var heightPercentage = screenHeight <= 768 ? 0.90 : screenHeight <= 1080 ? 0.80 : 0.75;

        var baseWidth = screenWidth * widthPercentage;
        var baseHeight = screenHeight * heightPercentage;

        var balancedWidth = Math.Max(Math.Min(screenWidth * 0.55, 1024), Math.Min(baseWidth, Math.Min(screenWidth * 0.95, 1920)));
        var balancedHeight = Math.Max(Math.Min(screenHeight * 0.55, 720), Math.Min(baseHeight, Math.Min(screenHeight * 0.95, 1100)));

        _window.Width = Math.Max(_window.MinWidth, balancedWidth);
        _window.Height = Math.Max(_window.MinHeight, balancedHeight);

        _window.Left = Math.Max(0, (workingArea.Width - _window.Width) / 2 + workingArea.Left);
        _window.Top = Math.Max(0, (workingArea.Height - _window.Height) / 2 + workingArea.Top);
        
        EnsureWindowVisible(workingArea);
    }

    private void EnsureWindowVisible(Rect workingArea)
    {
        if (_window.Left + _window.Width > workingArea.Right)
            _window.Left = workingArea.Right - _window.Width;
        if (_window.Top + _window.Height > workingArea.Bottom)
            _window.Top = workingArea.Bottom - _window.Height;
    }

    public void UpdateResponsiveLayout()
    {
        try
        {
            // Force update layout if root element is available
            _rootElement?.UpdateLayout();
            
            // Additional responsive logic can be added here
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating responsive layout: {ex.Message}");
        }
    }

    /// <summary>
    /// Captures the current window state into settings and schedules a debounced save.
    /// Multiple rapid calls (during resize/drag) are coalesced into a single save.
    /// </summary>
    public void SaveWindowState()
    {
        if (_window == null) return;

        try
        {
            var customization = _settingsService.Settings.CustomizationSettings;

            if (customization.RememberWindowState && _window.WindowState != WindowState.Minimized)
            {
                if (_window.WindowState == WindowState.Normal)
                {
                    customization.WindowWidth = _window.Width;
                    customization.WindowHeight = _window.Height;
                    customization.WindowLeft = _window.Left;
                    customization.WindowTop = _window.Top;
                }

                customization.WindowState = (int)_window.WindowState;
                ScheduleSave();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving window state");
        }
    }

    private void ScheduleSave()
    {
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    private void OnDebouncedSave(object sender, EventArgs e)
    {
        _saveDebounceTimer.Stop();
        _ = _settingsService.SaveAsync();
    }
}
