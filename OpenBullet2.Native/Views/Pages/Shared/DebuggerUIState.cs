using System.Windows;

namespace OpenBullet2.Native.Views.Pages.Shared;

/// <summary>
/// Encapsulates all UI visibility state for the debugger.
/// Extracted from Debugger.xaml.cs to reduce field clutter.
/// </summary>
public sealed class DebuggerUIState
{
    #region Current State
    /// <summary>
    /// Whether tab buttons (Log, Variables, HTML) are visible.
    /// </summary>
    public bool AreTabButtonsVisible { get; set; }

    /// <summary>
    /// Whether the secondary options panel is visible.
    /// </summary>
    public bool AreOptionsVisible { get; set; }

    /// <summary>
    /// Whether stacker controls are visible.
    /// </summary>
    public bool AreStackerControlsVisible { get; set; } = true;

    /// <summary>
    /// Whether focus mode is currently enabled.
    /// </summary>
    public bool IsFocusModeEnabled { get; set; }
    #endregion

    #region Focus Mode Stored State
    /// <summary>
    /// Stores the previous options visibility before entering focus mode.
    /// </summary>
    public bool FocusStoredOptionsVisible { get; set; }

    /// <summary>
    /// Stores the previous tab buttons visibility before entering focus mode.
    /// </summary>
    public bool FocusStoredTabButtonsVisible { get; set; }

    /// <summary>
    /// Stores the previous stacker visibility before entering focus mode.
    /// </summary>
    public bool FocusStoredStackerVisible { get; set; } = true;

    /// <summary>
    /// Stores the previous Stop button visibility before entering focus mode.
    /// </summary>
    public Visibility FocusStoredStopButtonVisibility { get; set; } = Visibility.Visible;

    /// <summary>
    /// Stores the previous Step button visibility before entering focus mode.
    /// </summary>
    public Visibility FocusStoredStepButtonVisibility { get; set; } = Visibility.Visible;

    /// <summary>
    /// Stores the previous Variables tab visibility before entering focus mode.
    /// </summary>
    public Visibility FocusStoredVariablesTabVisibility { get; set; } = Visibility.Visible;

    /// <summary>
    /// Stores the previous HTML tab visibility before entering focus mode.
    /// </summary>
    public Visibility FocusStoredHtmlTabVisibility { get; set; } = Visibility.Visible;

    /// <summary>
    /// Stores the previous Tab toggle button visibility before entering focus mode.
    /// </summary>
    public Visibility FocusStoredTabToggleVisibility { get; set; } = Visibility.Visible;

    /// <summary>
    /// Stores the previous Options toggle button visibility before entering focus mode.
    /// </summary>
    public Visibility FocusStoredOptionsToggleVisibility { get; set; } = Visibility.Visible;

    /// <summary>
    /// Stores the previous Stacker toggle button visibility before entering focus mode.
    /// </summary>
    public Visibility FocusStoredStackerToggleVisibility { get; set; } = Visibility.Visible;

    /// <summary>
    /// Stores the previous top menu visibility before entering focus mode.
    /// </summary>
    public Visibility FocusStoredTopMenuVisibility { get; set; } = Visibility.Visible;

    /// <summary>
    /// Stores the previous stacker controls panel visibility before entering focus mode.
    /// </summary>
    public Visibility FocusStoredStackerControlsVisibility { get; set; } = Visibility.Visible;
    #endregion

    /// <summary>
    /// Captures the current state before entering focus mode.
    /// </summary>
    public void CaptureStateForFocusMode()
    {
        FocusStoredOptionsVisible = AreOptionsVisible;
        FocusStoredTabButtonsVisible = AreTabButtonsVisible;
        FocusStoredStackerVisible = AreStackerControlsVisible;
    }

    /// <summary>
    /// Restores the captured state after exiting focus mode.
    /// </summary>
    public void RestoreStateFromFocusMode()
    {
        // Note: Actual UI updates are handled by the caller
        // This method provides the stored values for restoration
    }

    /// <summary>
    /// Resets all state to defaults.
    /// </summary>
    public void Reset()
    {
        AreTabButtonsVisible = false;
        AreOptionsVisible = false;
        AreStackerControlsVisible = true;
        IsFocusModeEnabled = false;
        
        FocusStoredOptionsVisible = false;
        FocusStoredTabButtonsVisible = false;
        FocusStoredStackerVisible = true;
        FocusStoredStopButtonVisibility = Visibility.Visible;
        FocusStoredStepButtonVisibility = Visibility.Visible;
        FocusStoredVariablesTabVisibility = Visibility.Visible;
        FocusStoredHtmlTabVisibility = Visibility.Visible;
        FocusStoredTabToggleVisibility = Visibility.Visible;
        FocusStoredOptionsToggleVisibility = Visibility.Visible;
        FocusStoredStackerToggleVisibility = Visibility.Visible;
        FocusStoredTopMenuVisibility = Visibility.Visible;
        FocusStoredStackerControlsVisibility = Visibility.Visible;
    }
}
