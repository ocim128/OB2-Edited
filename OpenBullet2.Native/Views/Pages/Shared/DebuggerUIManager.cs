using MahApps.Metro.IconPacks;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenBullet2.Native.Views.Pages.Shared;

/// <summary>
/// Manages UI visibility states and focus mode for the debugger.
/// Extracted from Debugger.xaml.cs to reduce code-behind complexity.
/// </summary>
public sealed class DebuggerUIManager
{
    #region Private Fields
    private readonly DebuggerUIState _state;
    private readonly Func<MainWindow?> _getMainWindow;
    #endregion

    #region UI Element References (set via Initialize)
    private Button? _tabToggleButton;
    private Button? _optionsToggleButton;
    private Button? _stackerToggleButton;
    private Button? _focusModeButton;
    private Button? _logTabButton;
    private Button? _variablesTabButton;
    private Button? _htmlTabButton;
    private Button? _stopButton;
    private Button? _stepButton;
    private Grid? _secondaryOptionsGrid;
    private Border? _searchControlsArea;
    private TabItem? _variablesTabItem;
    private TabItem? _htmlTabItem;
    private TabItem? _logTabItem;
    private TabControl? _tabControl;
    private PackIconUnicons? _tabToggleIcon;
    private PackIconUnicons? _optionsToggleIcon;
    private PackIconUnicons? _stackerToggleIcon;
    private PackIconUnicons? _focusModeIcon;
    private TextBlock? _focusModeText;
    #endregion

    public DebuggerUIState State => _state;

    public DebuggerUIManager(Func<MainWindow?> getMainWindow)
    {
        _state = new DebuggerUIState();
        _getMainWindow = getMainWindow ?? throw new ArgumentNullException(nameof(getMainWindow));
    }

    /// <summary>
    /// Initializes references to UI elements. Must be called after InitializeComponent.
    /// </summary>
    public void Initialize(
        Button? tabToggleButton,
        Button? optionsToggleButton,
        Button? stackerToggleButton,
        Button? focusModeButton,
        Button? logTabButton,
        Button? variablesTabButton,
        Button? htmlTabButton,
        Button? stopButton,
        Button? stepButton,
        Grid? secondaryOptionsGrid,
        Border? searchControlsArea,
        TabItem? variablesTabItem,
        TabItem? htmlTabItem,
        TabItem? logTabItem,
        TabControl? tabControl,
        PackIconUnicons? tabToggleIcon,
        PackIconUnicons? optionsToggleIcon,
        PackIconUnicons? stackerToggleIcon,
        PackIconUnicons? focusModeIcon,
        TextBlock? focusModeText)
    {
        _tabToggleButton = tabToggleButton;
        _optionsToggleButton = optionsToggleButton;
        _stackerToggleButton = stackerToggleButton;
        _focusModeButton = focusModeButton;
        _logTabButton = logTabButton;
        _variablesTabButton = variablesTabButton;
        _htmlTabButton = htmlTabButton;
        _stopButton = stopButton;
        _stepButton = stepButton;
        _secondaryOptionsGrid = secondaryOptionsGrid;
        _searchControlsArea = searchControlsArea;
        _variablesTabItem = variablesTabItem;
        _htmlTabItem = htmlTabItem;
        _logTabItem = logTabItem;
        _tabControl = tabControl;
        _tabToggleIcon = tabToggleIcon;
        _optionsToggleIcon = optionsToggleIcon;
        _stackerToggleIcon = stackerToggleIcon;
        _focusModeIcon = focusModeIcon;
        _focusModeText = focusModeText;
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

        if (_secondaryOptionsGrid != null)
        {
            _secondaryOptionsGrid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateOptionsToggleAppearance();
    }

    private void UpdateOptionsToggleAppearance()
    {
        if (_optionsToggleButton?.Content is not StackPanel content || content.Children.Count < 2)
        {
            return;
        }

        if (content.Children[1] is not TextBlock label)
        {
            return;
        }

        if (_state.AreOptionsVisible)
        {
            if (_optionsToggleIcon != null) _optionsToggleIcon.Kind = PackIconUniconsKind.EyeSlash;
            label.Text = "Hide Options";
            _optionsToggleButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }
        else
        {
            if (_optionsToggleIcon != null) _optionsToggleIcon.Kind = PackIconUniconsKind.Eye;
            label.Text = "Show Options";
            _optionsToggleButton.Background = new SolidColorBrush(Color.FromRgb(124, 58, 237));
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

        if (_logTabButton != null) _logTabButton.Visibility = targetVisibility;
        if (_variablesTabButton != null) _variablesTabButton.Visibility = targetVisibility;
        if (_htmlTabButton != null) _htmlTabButton.Visibility = targetVisibility;
        if (_searchControlsArea != null) _searchControlsArea.Visibility = targetVisibility;

        UpdateTabToggleAppearance();
    }

    private void UpdateTabToggleAppearance()
    {
        if (_tabToggleButton?.Content is not StackPanel content || content.Children.Count < 2)
        {
            return;
        }

        if (content.Children[1] is not TextBlock label)
        {
            return;
        }

        if (_state.AreTabButtonsVisible)
        {
            if (_tabToggleIcon != null) _tabToggleIcon.Kind = PackIconUniconsKind.EyeSlash;
            label.Text = "Hide UI";
            _tabToggleButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }
        else
        {
            if (_tabToggleIcon != null) _tabToggleIcon.Kind = PackIconUniconsKind.Eye;
            label.Text = "Show UI";
            _tabToggleButton.Background = new SolidColorBrush(Color.FromRgb(5, 150, 105));
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
        if (_stackerToggleButton?.Content is not StackPanel content || content.Children.Count < 2)
        {
            return;
        }

        if (content.Children[1] is not TextBlock label)
        {
            return;
        }

        if (_state.AreStackerControlsVisible)
        {
            if (_stackerToggleIcon != null) _stackerToggleIcon.Kind = PackIconUniconsKind.EyeSlash;
            label.Text = "Hide Stacker";
            _stackerToggleButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }
        else
        {
            if (_stackerToggleIcon != null) _stackerToggleIcon.Kind = PackIconUniconsKind.Eye;
            label.Text = "Show Stacker";
            _stackerToggleButton.Background = new SolidColorBrush(Color.FromRgb(5, 150, 105));
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
        // Capture current state
        _state.CaptureStateForFocusMode();
        _state.FocusStoredStopButtonVisibility = _stopButton?.Visibility ?? Visibility.Visible;
        _state.FocusStoredStepButtonVisibility = _stepButton?.Visibility ?? Visibility.Visible;
        _state.FocusStoredVariablesTabVisibility = _variablesTabItem?.Visibility ?? Visibility.Visible;
        _state.FocusStoredHtmlTabVisibility = _htmlTabItem?.Visibility ?? Visibility.Visible;
        _state.FocusStoredTabToggleVisibility = _tabToggleButton?.Visibility ?? Visibility.Visible;
        _state.FocusStoredOptionsToggleVisibility = _optionsToggleButton?.Visibility ?? Visibility.Visible;
        _state.FocusStoredStackerToggleVisibility = _stackerToggleButton?.Visibility ?? Visibility.Visible;

        var mainWindow = _getMainWindow();
        if (mainWindow?.topMenu != null)
        {
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

        _stopButton?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        _stepButton?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        _variablesTabItem?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        _htmlTabItem?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        if (_tabControl != null) _tabControl.SelectedItem = _logTabItem;

        if (_tabToggleButton != null) _tabToggleButton.Visibility = Visibility.Collapsed;
        if (_optionsToggleButton != null) _optionsToggleButton.Visibility = Visibility.Collapsed;
        if (_stackerToggleButton != null) _stackerToggleButton.Visibility = Visibility.Collapsed;

        UpdateFocusModeButtonAppearance(true);
    }

    private void ExitFocusMode()
    {
        // Restore captured state
        ApplyOptionsVisibility(_state.FocusStoredOptionsVisible);
        ApplyTabUiVisibility(_state.FocusStoredTabButtonsVisible);
        ApplyStackerVisibility(_state.FocusStoredStackerVisible);

        var mainWindow = _getMainWindow();
        if (mainWindow?.topMenu != null)
        {
            mainWindow.topMenu.SetCurrentValue(UIElement.VisibilityProperty, _state.FocusStoredTopMenuVisibility);
        }

        mainWindow?.ConfigEditorPage?.GetStackerControlsPanel()?.SetCurrentValue(
            UIElement.VisibilityProperty, _state.FocusStoredStackerControlsVisibility);

        _stopButton?.SetCurrentValue(UIElement.VisibilityProperty, _state.FocusStoredStopButtonVisibility);
        _stepButton?.SetCurrentValue(UIElement.VisibilityProperty, _state.FocusStoredStepButtonVisibility);
        _variablesTabItem?.SetCurrentValue(UIElement.VisibilityProperty, _state.FocusStoredVariablesTabVisibility);
        _htmlTabItem?.SetCurrentValue(UIElement.VisibilityProperty, _state.FocusStoredHtmlTabVisibility);

        if (_tabToggleButton != null) _tabToggleButton.Visibility = _state.FocusStoredTabToggleVisibility;
        if (_optionsToggleButton != null) _optionsToggleButton.Visibility = _state.FocusStoredOptionsToggleVisibility;
        if (_stackerToggleButton != null) _stackerToggleButton.Visibility = _state.FocusStoredStackerToggleVisibility;

        UpdateFocusModeButtonAppearance(false);
    }

    private void UpdateFocusModeButtonAppearance(bool isFocusMode)
    {
        if (isFocusMode)
        {
            if (_focusModeIcon != null) _focusModeIcon.Kind = PackIconUniconsKind.EyeSlash;
            if (_focusModeText != null) _focusModeText.Text = "Exit Focus";
            if (_focusModeButton != null) _focusModeButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }
        else
        {
            if (_focusModeIcon != null) _focusModeIcon.Kind = PackIconUniconsKind.Crosshair;
            if (_focusModeText != null) _focusModeText.Text = "Focus Mode";
            if (_focusModeButton != null) _focusModeButton.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11));
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
