using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Flux.Core.Services;
using Flux.Shared.Abstractions;
using Flux.Shared.Models;

namespace Flux.Shared.Services;

public class SettingsFacade : ISettingsFacade
{
    private readonly FluxSettingsService _fluxSettings;
    private readonly ILogger<SettingsFacade> _logger;

    public SettingsFacade(FluxSettingsService fluxSettings, ILogger<SettingsFacade> logger)
    {
        _fluxSettings = fluxSettings;
        _logger = logger;
    }

    public Task<SettingsSnapshotDto> GetSettingsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Map());

    public async Task<SettingsSnapshotDto> UpdateAsync(UpdateSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var settings = _fluxSettings.Settings;

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

        await _fluxSettings.SaveAsync().ConfigureAwait(false);
        _logger.LogInformation("Settings updated");
        return Map();
    }

    private SettingsSnapshotDto Map()
    {
        var settings = _fluxSettings.Settings;
        return new SettingsSnapshotDto(
            settings.CustomizationSettings.Theme,
            settings.SecuritySettings.RequireAdminLogin,
            settings.SecuritySettings.AdminUsername,
            settings.SecuritySettings.AdminSessionLifetimeHours,
            settings.SecuritySettings.GuestSessionLifetimeHours,
            settings.SecuritySettings.HttpsRedirect);
    }
}
