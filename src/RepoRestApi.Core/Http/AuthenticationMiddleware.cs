using System;
using System.Security.Cryptography;
using System.Text;
namespace RepoRestApi.Core.Http;
public static class AuthenticationMiddleware
{
    public static string GenerateToken() { var bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return Convert.ToBase64String(bytes); }
    public static bool Authenticate(string? authorization, string token)
    {
        if (authorization == null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        using var sha = SHA256.Create();
        var a = sha.ComputeHash(Encoding.UTF8.GetBytes(authorization.Substring(7)));
        var b = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
        int diff = 0; for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i]; return diff == 0;
    }
}
