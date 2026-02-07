using Flux.Core.Models.Settings;

namespace Flux.Web.Dtos.Settings;

/// <summary>
/// Settings for the Flux application.
/// </summary>
public class FluxSettingsDto
{
    /// <summary>
    /// General settings.
    /// </summary>
    public OBGeneralSettingsDto GeneralSettings { get; set; } = new();

    /// <summary>
    /// Settings related to remote repositories.
    /// </summary>
    public RemoteSettings RemoteSettings { get; set; } = new();

    /// <summary>
    /// Settings related to security.
    /// </summary>
    public OBSecuritySettingsDto SecuritySettings { get; set; } = new();

    /// <summary>
    /// Settings related to the appearance of the UI.
    /// </summary>
    public OBCustomizationSettingsDto CustomizationSettings { get; set; } = new();
}
