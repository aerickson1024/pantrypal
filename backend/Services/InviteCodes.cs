using System.Security.Cryptography;
using System.Text;

namespace PantryPal.Api.Services;

public static class InviteCodes
{
    public static bool TryParse(string value, out Guid code) =>
        Guid.TryParse(value, out code) && code != Guid.Empty;

    // Hashes the lowercase hyphenated form so every accepted spelling of a GUID maps to one row.
    public static string Hash(Guid code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.ToString("D")))).ToLowerInvariant();
}
