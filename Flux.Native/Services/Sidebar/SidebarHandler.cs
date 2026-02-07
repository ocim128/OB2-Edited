using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Flux.Native.Services.Sidebar;

public class SidebarHandler : ISidebarHandler
{
    private ColumnDefinition _sidebarColumn;
    private RotateTransform _toggleIconRotation;
    private FrameworkElement[] _textElements;
    private FrameworkElement[] _sectionHeaders;
    private FrameworkElement _sidebarHeader;
    private FrameworkElement _versionText;
    private FrameworkElement _bottomSeparator;
    private FrameworkElement _configSubmenu;
    private FrameworkElement _configsChevron;
    private readonly SidebarAnimator _animator;

    private bool _isCollapsed = true;

    public event System.EventHandler<bool> SidebarStateChanged;

    public bool IsCollapsed => _isCollapsed;

    public SidebarHandler(SidebarAnimator animator)
    {
        _animator = animator;
    }

    public void Initialize(
        ColumnDefinition sidebarColumn,
        RotateTransform toggleIconRotation,
        FrameworkElement[] textElements,
        FrameworkElement[] sectionHeaders,
        FrameworkElement sidebarHeader,
        FrameworkElement versionText,
        FrameworkElement bottomSeparator,
        FrameworkElement configSubmenu,
        FrameworkElement configsChevron)
    {
        _sidebarColumn = sidebarColumn;
        _toggleIconRotation = toggleIconRotation;
        _textElements = textElements;
        _sectionHeaders = sectionHeaders;
        _sidebarHeader = sidebarHeader;
        _versionText = versionText;
        _bottomSeparator = bottomSeparator;
        _configSubmenu = configSubmenu;
        _configsChevron = configsChevron;
    }

    public void Toggle()
    {
        SetCollapsed(!_isCollapsed);
    }

    public void SetCollapsed(bool collapsed)
    {
        _isCollapsed = collapsed;

        var targetWidth = _isCollapsed ? 60.0 : 220.0;
        var currentWidth = _sidebarColumn.Width.Value;

        // Animate toggle icon rotation
        var rotationAnimation = new DoubleAnimation
        {
            From = _isCollapsed ? 0 : 180,
            To = _isCollapsed ? 180 : 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        _toggleIconRotation.BeginAnimation(RotateTransform.AngleProperty, rotationAnimation);

        // Animate column width
        _animator.AnimateWidth(currentWidth, targetWidth, width => _sidebarColumn.Width = new GridLength(width));

        // Toggle visibility of text elements
        var textVisibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SetTextVisibility(textVisibility);

        // Update section headers
        foreach (var header in _sectionHeaders)
        {
            if (header != null) header.Visibility = textVisibility;
        }

        // Update header
        if (_sidebarHeader != null) _sidebarHeader.Visibility = textVisibility;
        if (_versionText != null) _versionText.Visibility = textVisibility;
        if (_bottomSeparator != null) _bottomSeparator.Visibility = textVisibility;

        // Hide submenu when collapsed
        if (_isCollapsed)
        {
            if (_configSubmenu != null) _configSubmenu.Visibility = Visibility.Collapsed;
            if (_configsChevron != null) _configsChevron.Visibility = Visibility.Collapsed;
        }
        else
        {
            if (_configsChevron != null) _configsChevron.Visibility = Visibility.Visible;
        }

        SidebarStateChanged?.Invoke(this, _isCollapsed);
    }

    public void InitializeCollapsedState()
    {
        if (_isCollapsed)
        {
            var textVisibility = Visibility.Collapsed;
            SetTextVisibility(textVisibility);

            foreach (var header in _sectionHeaders)
            {
                if (header != null) header.Visibility = textVisibility;
            }

            if (_sidebarHeader != null) _sidebarHeader.Visibility = textVisibility;
            if (_versionText != null) _versionText.Visibility = textVisibility;
            if (_bottomSeparator != null) _bottomSeparator.Visibility = textVisibility;
            if (_configSubmenu != null) _configSubmenu.Visibility = Visibility.Collapsed;
            if (_configsChevron != null) _configsChevron.Visibility = Visibility.Collapsed;

            if (_toggleIconRotation != null) _toggleIconRotation.Angle = 180;
        }
    }

    private void SetTextVisibility(Visibility visibility)
    {
        foreach (var element in _textElements)
        {
            if (element != null) element.Visibility = visibility;
        }
    }
}
