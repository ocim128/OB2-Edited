namespace OpenBullet2.Shared.Models;

public record UpdateSettingsRequest(
    string? Theme,
    bool? RequireAdminLogin,
    string? AdminUsername,
    string? AdminPassword,
    int? AdminSessionLifetimeHours,
    int? GuestSessionLifetimeHours,
    bool? HttpsRedirect);
