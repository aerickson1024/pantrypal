using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PantryPal.Api.Data;

namespace PantryPal.Api.Services;

public sealed class AccountService(
    PantryPalDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<AccountService> logger) : IAccountService
{
    private const string PasswordHashPrefix = "pbkdf2-sha256";
    private const int PasswordIterations = 210000;
    private const int PasswordSaltSize = 16;
    private const int PasswordKeySize = 32;

    private readonly IDataProtector geminiKeyProtector =
        dataProtectionProvider.CreateProtector("PantryPal.GeminiApiKey.v1");

    public async Task<RegisterResult> RegisterAsync(
        string email,
        string password,
        string firstName,
        string lastName,
        string inviteCode,
        CancellationToken cancellationToken)
    {
        if (!InviteCodes.TryParse(inviteCode, out var code))
        {
            return new RegisterResult(RegisterOutcome.InvalidInviteCode, null);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var codeHash = InviteCodes.Hash(code);
        var invite = await dbContext.InviteCodes
            .FirstOrDefaultAsync(code => code.CodeHash == codeHash && code.UsedAt == null, cancellationToken);
        if (invite is null)
        {
            return new RegisterResult(RegisterOutcome.InvalidInviteCode, null);
        }

        if (await dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken))
        {
            return new RegisterResult(RegisterOutcome.EmailTaken, null);
        }

        var user = new User
        {
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            PasswordHash = HashPassword(password),
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique email index caught a concurrent registration.
            return new RegisterResult(RegisterOutcome.EmailTaken, null);
        }

        var consumed = await dbContext.InviteCodes
            .Where(code => code.Id == invite.Id && code.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(code => code.UsedByUserId, (ulong?)user.Id)
                .SetProperty(code => code.UsedAt, (DateTime?)DateTime.UtcNow),
                cancellationToken);

        if (consumed != 1)
        {
            // Another request used the same invite code first. Rolling back removes the new user.
            return new RegisterResult(RegisterOutcome.InvalidInviteCode, null);
        }

        await transaction.CommitAsync(cancellationToken);
        return new RegisterResult(RegisterOutcome.Created, user.Id);
    }

    public async Task<ulong?> VerifyCredentialsAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
        if (user is null)
        {
            return null;
        }

        return VerifyPassword(password, user.PasswordHash) ? user.Id : null;
    }

    public async Task<AccountProfile?> GetProfileAsync(ulong userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        return user is null
            ? null
            : new AccountProfile(user.Email, user.FirstName, user.LastName, !string.IsNullOrEmpty(user.ProtectedGeminiApiKey));
    }

    public async Task<bool> UpdateNameAsync(ulong userId, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var updated = await dbContext.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.FirstName, firstName)
                .SetProperty(user => user.LastName, lastName), cancellationToken);

        return updated == 1;
    }

    public async Task<string?> GetGeminiApiKeyAsync(ulong userId, CancellationToken cancellationToken)
    {
        var protectedKey = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.ProtectedGeminiApiKey)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(protectedKey))
        {
            return null;
        }

        try
        {
            return geminiKeyProtector.Unprotect(protectedKey);
        }
        catch (CryptographicException exception)
        {
            // Happens when the Data Protection keys folder was lost or changed.
            logger.LogWarning(exception, "A saved Gemini API key could not be decrypted for user {UserId}.", userId);
            return null;
        }
    }

    public async Task<bool> SetGeminiApiKeyAsync(ulong userId, string apiKey, CancellationToken cancellationToken)
    {
        var protectedKey = geminiKeyProtector.Protect(apiKey);
        var updated = await dbContext.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.ProtectedGeminiApiKey, protectedKey), cancellationToken);

        return updated == 1;
    }

    public async Task<bool> ClearGeminiApiKeyAsync(ulong userId, CancellationToken cancellationToken)
    {
        var updated = await dbContext.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.ProtectedGeminiApiKey, (string?)null), cancellationToken);

        return updated == 1;
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(PasswordSaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            PasswordIterations,
            HashAlgorithmName.SHA256,
            PasswordKeySize);

        return string.Join(
            '$',
            PasswordHashPrefix,
            PasswordIterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4
            || parts[0] != PasswordHashPrefix
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations <= 0)
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
