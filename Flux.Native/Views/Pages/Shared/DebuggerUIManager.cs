using MahApps.Metro.IconPacks;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Flux.Native.Views.Pages.Shared;

/// <summary>
/// Manages UI visibility states and focus mode for the debugger.
/// Extracted from Debugger.xaml.cs to reduce code-behind complexity.
/// </summary>
public sealed class DebuggerUIManager
{
    #region Private Fields
    private readonly DebuggerUIState _state;
    private readonly Func<MainWindow?> _getMainWindow;
    private Debugger? _page;
    #endregion

    public DebuggerUIState State => _state;

    public DebuggerUIManager(Func<MainWindow?> getMainWindow)
    {
        _state = new DebuggerUIState();
        _getMainWindow = getMainWindow ?? throw new ArgumentNullException(nameof(getMainWindow));
    }

    /// <summary>
    /// Initializes references to UI elements via the page instance. Must be called after InitializeComponent.
    /// </summary>
    public void Initialize(Debugger page)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
    }

    #region Options Visibility
    /// <summary>
    /// Toggles the visibility of the options panel.
    /// </summary>
    public void ToggleOptions()
    {
        ApplyOptionsVisibility(!_state.AreOptionsVisible);
    }

    /// <summary>
    /// Sets the visibility of the options panel.
    /// </summary>
    public void ApplyOptionsVisibility(bool visible)
    {
        _state.AreOptionsVisible = visible;

        if (_page?.SecondaryOptionsGrid != null)
        {
            _page.SecondaryOptionsGrid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateOptionsToggleAppearance();
    }

    private void UpdateOptionsToggleAppearance()
    {
        if (_page?.OptionsToggleButton?.Content is not StackPanel content || content.Children.Count < 2)
        {
            return;
        }

        if (content.Children[1] is not TextBlock label)
        {
            return;
        }

        if (_state.AreOptionsVisible)
        {
            if (_page.OptionsToggleIcon != null) _page.OptionsToggleIcon.Kind = PackIconUniconsKind.EyeSlash;
            label.Text = "Hide Options";
            _page.OptionsToggleButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }
        else
        {
            if (_page.OptionsToggleIcon != null) _page.OptionsToggleIcon.Kind = PackIconUniconsKind.Eye;
            label.Text = "Show Options";
            _page.OptionsToggleButton.Background = new SolidColorBrush(Color.FromRgb(124, 58, 237));
        }
    }
    #endregion

    #region Tab UI Visibility
    /// <summary>
    /// Toggles the visibility of tab buttons and search controls.
    /// </summary>
    public void ToggleTabButtons()
    {
        ApplyTabUiVisibility(!_state.AreTabButtonsVisible);
    }

    /// <summary>
    /// Sets the visibility of tab buttons and search controls.
    /// </summary>
    public void ApplyTabUiVisibility(bool visible)
    {
        _state.AreTabButtonsVisible = visible;
        var targetVisibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (_page != null)
        {
            if (_page.LogTabButton != null) _page.LogTabButton.Visibility = targetVisibility;
            if (_page.VariablesTabButton != null) _page.VariablesTabButton.Visibility = targetVisibility;
            if (_page.HtmlTabButton != null) _page.HtmlTabButton.Visibility = targetVisibility;
            if (_page.SearchControlsArea != null) _page.SearchControlsArea.Visibility = targetVisibility;
        }

        UpdateTabToggleAppearance();
    }

    private void UpdateTabToggleAppearance()
    {
        if (_page?.TabToggleButton?.Content is not StackPanel content || content.Children.Count < 2)
        {
            return;
        }

        if (content.Children[1] is not TextBlock label)
        {
            return;
        }

        if (_state.AreTabButtonsVisible)
        {
            if (_page.TabToggleIcon != null) _page.TabToggleIcon.Kind = PackIconUniconsKind.EyeSlash;
            label.Text = "Hide UI";
            _page.TabToggleButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }
        else
        {
            if (_page.TabToggleIcon != null) _page.TabToggleIcon.Kind = PackIconUniconsKind.Eye;
            label.Text = "Show UI";
            _page.TabToggleButton.Background = new SolidColorBrush(Color.FromRgb(5, 150, 105));
        }
    }
    #endregion

    #region Stacker Visibility
    /// <summary>
    /// Toggles the visibility of the stacker pane.
    /// </summary>
    public void ToggleStacker()
    {
        ApplyStackerVisibility(!_state.AreStackerControlsVisible);
    }

    /// <summary>
    /// Sets the visibility of the stacker pane.
    /// </summary>
    public void ApplyStackerVisibility(bool showStacker)
    {
        try
        {
            var mainWindow = _getMainWindow();
            var configStacker = mainWindow?.ConfigEditorPage?.GetStackerPage(ensureCreated: true);

            if (configStacker != null)
            {
                configStacker.SetStackerPaneVisibility(showStacker);
                _state.AreStackerControlsVisible = configStacker.IsStackerPaneVisible;
            }
            else
            {
                _state.AreStackerControlsVisible = showStacker;
            }
        }
        catch
        {
            _state.AreStackerControlsVisible = showStacker;
        }

        UpdateStackerToggleAppearance();
    }

    private void UpdateStackerToggleAppearance()
    {
        if (_page?.StackerToggleButton?.Content is not StackPanel content || content.Children.Count < 2)
        {
            return;
        }

        if (content.Children[1] is not TextBlock label)
        {
            return;
        }

        if (_state.AreStackerControlsVisible)
        {
            if (_page.StackerToggleIcon != null) _page.StackerToggleIcon.Kind = PackIconUniconsKind.EyeSlash;
            label.Text = "Hide Stacker";
            _page.StackerToggleButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }
        else
        {
            if (_page.StackerToggleIcon != null) _page.StackerToggleIcon.Kind = PackIconUniconsKind.Eye;
            label.Text = "Show Stacker";
            _page.StackerToggleButton.Background = new SolidColorBrush(Color.FromRgb(5, 150, 105));
        }
    }
    #endregion

    #region Focus Mode
    /// <summary>
    /// Toggles focus mode on/off.
    /// </summary>
    public void ToggleFocusMode()
    {
        ApplyFocusMode(!_state.IsFocusModeEnabled);
    }

    /// <summary>
    /// Applies focus mode state.
    /// </summary>
    public void ApplyFocusMode(bool enable)
    {
        if (_state.IsFocusModeEnabled == enable)
        {
            return;
        }

        if (enable)
        {
            EnterFocusMode();
        }
        else
        {
            ExitFocusMode();
        }

        _state.IsFocusModeEnabled = enable;
    }

    private void EnterFocusMode()
    {
        if (_page == null) return;

        // Capture current state
        _state.CaptureStateForFocusMode();
        _state.FocusStoredStopButtonVisibility = _page.StopButton?.Visibility ?? Visibility.Visible;
        _state.FocusStoredStepButtonVisibility = _page.StepButton?.Visibility ?? Visibility.Visible;
        _state.FocusStoredVariablesTabVisibility = _page.VariablesTabItem?.Visibility ?? Visibility.Visible;
        _state.FocusStoredHtmlTabVisibility = _page.HtmlTabItem?.Visibility ?? Visibility.Visible;
        _state.FocusStoredTabToggleVisibility = _page.TabToggleButton?.Visibility ?? Visibility.Visible;
        _state.FocusStoredOptionsToggleVisibility = _page.OptionsToggleButton?.Visibility ?? Visibility.Visible;
        _state.FocusStoredStackerToggleVisibility = _page.StackerToggleButton?.Visibility ?? Visibility.Visible;

        var mainWindow = _getMainWindow();
        
        // Hide entire sidebar in focus mode
        if (mainWindow?.SidebarBorder != null)
        {
            _state.FocusStoredTopMenuVisibility = mainWindow.SidebarBorder.Visibility;
            mainWindow.SidebarBorder.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            
            // Also collapse sidebar column width to give more space
            if (mainWindow.SidebarColumn != null)
            {
                mainWindow.SidebarColumn.MinWidth = 0;
                mainWindow.SidebarColumn.Width = new System.Windows.GridLength(0);
            }
        }
        else if (mainWindow?.topMenu != null)
        {
            // Fallback to old behavior if SidebarBorder not found
            _state.FocusStoredTopMenuVisibility = mainWindow.topMenu.Visibility;
            mainWindow.topMenu.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        }
        else
        {
            _state.FocusStoredTopMenuVisibility = Visibility.Visible;
        }

        var stackerControlsPanel = mainWindow?.ConfigEditorPage?.GetStackerControlsPanel();
        if (stackerControlsPanel != null)
        {
            _state.FocusStoredStackerControlsVisibility = stackerControlsPanel.Visibility;
            stackerControlsPanel.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        }
        else
        {
            _state.FocusStoredStackerControlsVisibility = Visibility.Visible;
        }

        // Apply minimal UI
        ApplyOptionsVisibility(false);
        ApplyTabUiVisibility(false);
        ApplyStackerVisibility(false);

        _page.StopButton?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        _page.StepButton?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        _page.VariablesTabItem?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        _page.HtmlTabItem?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        if (_page.tabControl != null) _page.tabControl.SelectedItem = _page.LogTabItem;

        if (_page.TabToggleButton != null) _page.TabToggleButton.Visibility = Visibility.Collapsed;
        if (_page.OptionsToggleButton != null) _page.OptionsToggleButton.Visibility = Visibility.Collapsed;
        if (_page.StackerToggleButton != null) _page.StackerToggleButton.Visibility = Visibility.Collapsed;

        UpdateFocusModeButtonAppearance(true);
    }

    private void ExitFocusMode()
    {
        if (_page == null) return;

        // Restore captured state
        ApplyOptionsVisibility(_state.FocusStoredOptionsVisible);
        ApplyTabUiVisibility(_state.FocusStoredTabButtonsVisible);
        ApplyStackerVisibility(_state.FocusStoredStackerVisible);

        var mainWindow = _getMainWindow();
        
        // Restore sidebar visibility
        if (mainWindow?.SidebarBorder != null)
        {
            mainWindow.SidebarBorder.SetCurrentValue(UIElement.VisibilityProperty, _state.FocusStoredTopMenuVisibility);
            
            // Restore sidebar column width (to collapsed state - 60px)
            if (mainWindow.SidebarColumn != null)
            {
                mainWindow.SidebarColumn.MinWidth = 60;
                mainWindow.SidebarColumn.Width = new System.Windows.GridLength(60);
            }
        }
        else if (mainWindow?.topMenu != null)
        {
            mainWindow.topMenu.SetCurrentValue(UIElement.VisibilityProperty, _state.FocusStoredTopMenuVisibility);
        }

        mainWindow?.ConfigEditorPage?.GetStackerControlsPanel()?.SetCurrentValue(
            UIElement.VisibilityProperty, _state.FocusStoredStackerControlsVisibility);

        _page.StopButton?.SetCurrentValue(UIElement.VisibilityProperty, _state.FocusStoredStopButtonVisibility);
        _page.StepButton?.SetCurrentValue(UIElement.VisibilityProperty, _state.FocusStoredStepButtonVisibility);
        _page.VariablesTabItem?.SetCurrentValue(UIElement.VisibilityProperty, _state.FocusStoredVariablesTabVisibility);
        _page.HtmlTabItem?.SetCurrentValue(UIElement.VisibilityProperty, _state.FocusStoredHtmlTabVisibility);

        if (_page.TabToggleButton != null) _page.TabToggleButton.Visibility = _state.FocusStoredTabToggleVisibility;
        if (_page.OptionsToggleButton != null) _page.OptionsToggleButton.Visibility = _state.FocusStoredOptionsToggleVisibility;
        if (_page.StackerToggleButton != null) _page.StackerToggleButton.Visibility = _state.FocusStoredStackerToggleVisibility;

        UpdateFocusModeButtonAppearance(false);
    }

    private void UpdateFocusModeButtonAppearance(bool isFocusMode)
    {
        if (isFocusMode)
        {
            if (_page?.FocusModeIcon != null) _page.FocusModeIcon.Kind = PackIconUniconsKind.EyeSlash;
            if (_page?.FocusModeText != null) _page.FocusModeText.Text = "Exit Focus";
            if (_page?.FocusModeButton != null) _page.FocusModeButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }
        else
        {
            if (_page?.FocusModeIcon != null) _page.FocusModeIcon.Kind = PackIconUniconsKind.Crosshair;
            if (_page?.FocusModeText != null) _page.FocusModeText.Text = "Focus Mode";
            if (_page?.FocusModeButton != null) _page.FocusModeButton.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        }
    }
    #endregion

    #region Initial UI Setup
    /// <summary>
    /// Updates all toggle button appearances to match current state.
    /// Call this after Initialize.
    /// </summary>
    public void UpdateAllToggleAppearances()
    {
        UpdateOptionsToggleAppearance();
        UpdateTabToggleAppearance();
        UpdateStackerToggleAppearance();
    }
    #endregion
}
