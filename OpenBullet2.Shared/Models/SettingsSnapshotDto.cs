namespace OpenBullet2.Shared.Models;

public record SettingsSnapshotDto(
    string Theme,
    bool RequireAdminLogin,
    string AdminUsername,
    int AdminSessionLifetimeHours,
    int GuestSessionLifetimeHours,
    bool HttpsRedirect);
