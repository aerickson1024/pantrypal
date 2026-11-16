namespace PantryPal.Api.Services;

public enum RegisterOutcome
{
    Created,
    EmailTaken,
    InvalidInviteCode
}

public sealed record RegisterResult(RegisterOutcome Outcome, ulong? UserId);

public sealed record AccountProfile(string Email, string FirstName, string LastName, bool HasGeminiKey);

public interface IAccountService
{
    Task<RegisterResult> RegisterAsync(
        string email,
        string password,
        string firstName,
        string lastName,
        string inviteCode,
        CancellationToken cancellationToken);

    Task<ulong?> VerifyCredentialsAsync(string email, string password, CancellationToken cancellationToken);

    Task<AccountProfile?> GetProfileAsync(ulong userId, CancellationToken cancellationToken);

    Task<bool> UpdateNameAsync(ulong userId, string firstName, string lastName, CancellationToken cancellationToken);

    Task<string?> GetGeminiApiKeyAsync(ulong userId, CancellationToken cancellationToken);

    Task<bool> SetGeminiApiKeyAsync(ulong userId, string apiKey, CancellationToken cancellationToken);

    Task<bool> ClearGeminiApiKeyAsync(ulong userId, CancellationToken cancellationToken);
}
