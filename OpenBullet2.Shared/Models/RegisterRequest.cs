namespace OpenBullet2.Shared.Models;

public record RegisterRequest(string Username, string Password, string Roles = "Admin");
