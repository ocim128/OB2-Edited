namespace Flux.Core.Models.Settings;

/// <summary>
/// Accessibility and usability preferences for the native client.
/// </summary>
public class AccessibilitySettings
{
    /// <summary>
    /// Scales the overall UI by this factor to aid readability.
    /// </summary>
    public double UiScale { get; set; } = 1.0;

    /// <summary>
    /// Toggles a high-contrast palette for better legibility.
    /// </summary>
    public bool EnableHighContrast { get; set; } = false;

    /// <summary>
    /// Forces visible focus rectangles on all interactive controls.
    /// </summary>
    public bool AlwaysShowFocusVisuals { get; set; } = true;

    /// <summary>
    /// Increases spacing around interactive controls to improve hit targets.
    /// </summary>
    public bool UseComfortableSpacing { get; set; } = true;

    /// <summary>
    /// Reduces non-essential transitions and animations.
    /// </summary>
    public bool ReduceAnimations { get; set; } = false;

    /// <summary>
    /// Enables larger fonts for code editors and rich text viewers.
    /// </summary>
    public bool UseLargeEditorFonts { get; set; } = true;

    /// <summary>
    /// Enables inline tooltips for critical buttons and toggles.
    /// </summary>
    public bool ShowHelpfulTooltips { get; set; } = true;
}