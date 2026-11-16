namespace PantryPal.Api.Data;

public sealed class User
{
    public ulong Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string? ProtectedGeminiApiKey { get; set; }

    public DateTime CreatedAt { get; set; }
}
