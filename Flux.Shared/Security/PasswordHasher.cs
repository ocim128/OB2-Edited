using System;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace Flux.Shared.Security;

public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 32;
    private const int KeySize = 32;

    public static (string Hash, string Salt) HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        var hashBytes = KeyDerivation.Pbkdf2(password, saltBytes, KeyDerivationPrf.HMACSHA512, Iterations, KeySize);
        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    public static string HashPassword(string password, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var hashBytes = KeyDerivation.Pbkdf2(password, saltBytes, KeyDerivationPrf.HMACSHA512, Iterations, KeySize);
        return Convert.ToBase64String(hashBytes);
    }

    public static bool Verify(string password, string hash, string salt)
        => HashPassword(password, salt) == hash;
}
