using System;
using System.IO;
using System.Text.Json;

namespace OpenBullet2.Native.Services
{
    public sealed class ModemPluginSettingsProvider
    {
        private readonly string settingsPath;
        private readonly object sync = new();

        public ModemPluginSettingsProvider()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var userData = Path.Combine(baseDir, "UserData");
            Directory.CreateDirectory(userData);
            settingsPath = Path.Combine(userData, "modem-plugin.json");
        }

        public ModemPluginSettings Load()
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var settings = JsonSerializer.Deserialize<ModemPluginSettings>(json);
                    if (settings != null)
                    {
                        return Normalize(settings);
                    }
                }
            }
            catch
            {
                // Ignore malformed files and fall back to defaults.
            }

            return new ModemPluginSettings();
        }

        public void Save(ModemPluginSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            lock (sync)
            {
                var normalized = Normalize(settings);
                var directory = Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsPath, json);
            }
        }

        private static ModemPluginSettings Normalize(ModemPluginSettings settings)
        {
            return new ModemPluginSettings
            {
                RouterAddress = string.IsNullOrWhiteSpace(settings.RouterAddress)
                    ? "http://192.168.0.1"
                    : settings.RouterAddress.Trim(),
                Username = string.IsNullOrWhiteSpace(settings.Username)
                    ? "admin"
                    : settings.Username.Trim()
            };
        }
    }

    public sealed class ModemPluginSettings
    {
        public string RouterAddress { get; set; } = "http://192.168.0.1";
        public string Username { get; set; } = "admin";
    }
}
