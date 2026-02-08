namespace Flux.Core.Models.Settings;

/// <summary>
/// Settings related to the appearance of the Flux GUI.
/// </summary>
public class CustomizationSettings
{
    /// <summary>
    /// The theme to use. Themes are included in separate files and identified
    /// by their name. Web UI only.
    /// </summary>
    public string Theme { get; set; } = "Default";

    /// <summary>
    /// The theme to use for the Monaco editor. Web UI only.
    /// </summary>
    public string MonacoTheme { get; set; } = "vs-dark";

    /// <summary>
    /// Whether to wrap words at viewport width.
    /// </summary>
    public bool WordWrap { get; set; } = false;

    /// <summary>
    /// The native desktop UI mode. Supported values: Light, Dark.
    /// </summary>
    public string NativeThemeMode { get; set; } = "Light";

    /// <summary>
    /// The main background color. Native UI only.
    /// </summary>
    public string BackgroundMain { get; set; } = "#F8FAFC";

    /// <summary>
    /// The background color for inputs. Native UI only.
    /// </summary>
    public string BackgroundInput { get; set; } = "#FFFFFF";

    /// <summary>
    /// The secondary background color. Native UI only.
    /// </summary>
    public string BackgroundSecondary { get; set; } = "#EEF2F7";

    /// <summary>
    /// The main foreground color. Native UI only.
    /// </summary>
    public string ForegroundMain { get; set; } = "#0F172A";

    /// <summary>
    /// The foreground color for inputs. Native UI only.
    /// </summary>
    public string ForegroundInput { get; set; } = "#0F172A";

    /// <summary>
    /// The foreground color for hits. Native UI only.
    /// </summary>
    public string ForegroundGood { get; set; } = "#10B981";

    /// <summary>
    /// The foreground color for fails. Native UI only.
    /// </summary>
    public string ForegroundBad { get; set; } = "#EF4444";

    /// <summary>
    /// The foreground color for custom hits. Native UI only.
    /// </summary>
    public string ForegroundCustom { get; set; } = "#F97316";

    /// <summary>
    /// The foreground color for retries. Native UI only.
    /// </summary>
    public string ForegroundRetry { get; set; } = "#EAB308";

    /// <summary>
    /// The foreground color for bans. Native UI only.
    /// </summary>
    public string ForegroundBanned { get; set; } = "#8B5CF6";

    /// <summary>
    /// The foreground color for hits to check. Native UI only.
    /// </summary>
    public string ForegroundToCheck { get; set; } = "#14B8A6";

    /// <summary>
    /// The foreground color for selected menu items. Native UI only.
    /// </summary>
    public string ForegroundMenuSelected { get; set; } = "#2563EB";

    /// <summary>
    /// The color of success buttons. Native UI only.
    /// </summary>
    public string SuccessButton { get; set; } = "#10B981";

    /// <summary>
    /// The color of primary buttons. Native UI only.
    /// </summary>
    public string PrimaryButton { get; set; } = "#2563EB";

    /// <summary>
    /// The color of warning buttons. Native UI only.
    /// </summary>
    public string WarningButton { get; set; } = "#F59E0B";

    /// <summary>
    /// The color of danger buttons. Native UI only.
    /// </summary>
    public string DangerButton { get; set; } = "#EF4444";

    /// <summary>
    /// The foreground color of buttons. Native UI only.
    /// </summary>
    public string ForegroundButton { get; set; } = "#0F172A";

    /// <summary>
    /// The background color of buttons. Native UI only.
    /// </summary>
    public string BackgroundButton { get; set; } = "#E2E8F0";

    /// <summary>
    /// The path to the background image. Native UI only.
    /// </summary>
    public string BackgroundImagePath { get; set; } = "";

    /// <summary>
    /// The opacity of the background image (from 0 to 100). Native UI only.
    /// </summary>
    public double BackgroundOpacity { get; set; } = 100;

    /// <summary>
    /// Whether to play a sound when a hit is found.
    /// </summary>
    public bool PlaySoundOnHit { get; set; } = false;

    /// <summary>
    /// The saved window width. Native UI only.
    /// </summary>
    public double WindowWidth { get; set; } = 1000;

    /// <summary>
    /// The saved window height. Native UI only.
    /// </summary>
    public double WindowHeight { get; set; } = 600;

    /// <summary>
    /// The saved window left position. Native UI only.
    /// </summary>
    public double WindowLeft { get; set; } = 100;

    /// <summary>
    /// The saved window top position. Native UI only.
    /// </summary>
    public double WindowTop { get; set; } = 100;

    /// <summary>
    /// The saved window state (Normal, Maximized, Minimized). Native UI only.
    /// </summary>
    public int WindowState { get; set; } = 0; // 0 = Normal, 1 = Minimized, 2 = Maximized

    /// <summary>
    /// Whether to remember window size and position. Native UI only.
    /// </summary>
    public bool RememberWindowState { get; set; } = true;

    /// <summary>
    /// Whether the sidebar is expanded or collapsed. Native UI only.
    /// </summary>
    public bool SidebarExpanded { get; set; } = false;
}
