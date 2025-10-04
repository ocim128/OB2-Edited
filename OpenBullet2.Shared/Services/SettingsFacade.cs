using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenBullet2.Core.Services;
using OpenBullet2.Shared.Abstractions;
using OpenBullet2.Shared.Models;

namespace OpenBullet2.Shared.Services;

public class SettingsFacade : ISettingsFacade
{
    private readonly OpenBulletSettingsService _obSettings;
    private readonly ILogger<SettingsFacade> _logger;

    public SettingsFacade(OpenBulletSettingsService obSettings, ILogger<SettingsFacade> logger)
    {
        _obSettings = obSettings;
        _logger = logger;
    }

    public Task<SettingsSnapshotDto> GetSettingsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Map());

    public async Task<SettingsSnapshotDto> UpdateAsync(UpdateSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var settings = _obSettings.Settings;

        if (request.Theme is not null)
        {
            settings.CustomizationSettings.Theme = request.Theme;
        }

        if (request.RequireAdminLogin.HasValue)
        {
            settings.SecuritySettings.RequireAdminLogin = request.RequireAdminLogin.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.AdminUsername))
        {
            settings.SecuritySettings.AdminUsername = request.AdminUsername;
        }

        if (!string.IsNullOrWhiteSpace(request.AdminPassword))
        {
            settings.SecuritySettings.SetupAdminPassword(request.AdminPassword);
            _logger.LogInformation("Admin password updated");
        }

        if (request.AdminSessionLifetimeHours.HasValue)
        {
            settings.SecuritySettings.AdminSessionLifetimeHours = request.AdminSessionLifetimeHours.Value;
        }

        if (request.GuestSessionLifetimeHours.HasValue)
        {
            settings.SecuritySettings.GuestSessionLifetimeHours = request.GuestSessionLifetimeHours.Value;
        }

        if (request.HttpsRedirect.HasValue)
        {
            settings.SecuritySettings.HttpsRedirect = request.HttpsRedirect.Value;
        }

        await _obSettings.SaveAsync().ConfigureAwait(false);
        _logger.LogInformation("Settings updated");
        return Map();
    }

    private SettingsSnapshotDto Map()
    {
        var settings = _obSettings.Settings;
        return new SettingsSnapshotDto(
            settings.CustomizationSettings.Theme,
            settings.SecuritySettings.RequireAdminLogin,
            settings.SecuritySettings.AdminUsername,
            settings.SecuritySettings.AdminSessionLifetimeHours,
            settings.SecuritySettings.GuestSessionLifetimeHours,
            settings.SecuritySettings.HttpsRedirect);
    }
}
