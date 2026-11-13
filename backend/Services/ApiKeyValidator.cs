using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PantryPal.Api.Data;

namespace PantryPal.Api.Services;

public sealed class ApiKeyValidator(PantryPalDbContext dbContext) : IApiKeyValidator
{
    public async Task<bool> IsValidAsync(string? apiKey, CancellationToken cancellationToken)
    {
        var trimmed = apiKey?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        // Matches the lowercase hex produced by MySQL SHA2(..., 256).
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed))).ToLowerInvariant();
        return await dbContext.ApiKeys
            .AsNoTracking()
            .AnyAsync(key => key.KeyHash == keyHash && key.IsActive, cancellationToken);
    }
}
