using Newtonsoft.Json;
using Flux.Core.Models.Settings;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Flux.Core.Services;

/// <summary>
/// Provides interaction with settings of the Flux application.
/// </summary>
public class FluxSettingsService
{
    private string BaseFolder { get; }
    private readonly JsonSerializerSettings jsonSettings;

    /// <summary>
    /// The path of the file where settings are saved.
    /// </summary>
    public string FileName => Path.Combine(BaseFolder, "FluxSettings.json");

    /// <summary>
    /// The actual settings. After modifying them, call the <see cref="SaveAsync"/> method to persist them.
    /// </summary>
    public FluxSettings Settings { get; private set; }

    public FluxSettingsService(string baseFolder)
    {
        BaseFolder = baseFolder;
        Directory.CreateDirectory(baseFolder);

        jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            TypeNameHandling = TypeNameHandling.Auto
        };

        if (File.Exists(FileName))
        {
            Settings = JsonConvert.DeserializeObject<FluxSettings>(File.ReadAllText(FileName), jsonSettings);

            // Backfill newly added settings when deserializing older files.
            Settings.AccessibilitySettings ??= new AccessibilitySettings();
        }
        else
        {
            Recreate();
            File.WriteAllText(FileName, JsonConvert.SerializeObject(Settings, jsonSettings));
        }
    }

    /// <summary>
    /// Saves the <see cref="Settings"/> to disk.
    /// </summary>
    public async Task SaveAsync() => await File.WriteAllTextAsync(FileName, JsonConvert.SerializeObject(Settings, jsonSettings));

    /// <summary>
    /// Restores the default <see cref="Settings"/> (does not save to disk).
    /// </summary>
    public void Recreate() => Settings = new FluxSettings
    {
        GeneralSettings = new GeneralSettings { ProxyCheckTargets = new List<ProxyCheckTarget> { new ProxyCheckTarget() } },
        RemoteSettings = new RemoteSettings(),
        SecuritySettings = new SecuritySettings().GenerateJwtKey().SetupAdminPassword("admin"),
        CustomizationSettings = new CustomizationSettings(),
        AccessibilitySettings = new AccessibilitySettings()
    };
}
