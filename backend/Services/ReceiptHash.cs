using System.Security.Cryptography;

namespace PantryPal.Api.Services;

public static class ReceiptHash
{
    public static string Compute(ReadOnlySpan<byte> imageBytes) =>
        Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
}