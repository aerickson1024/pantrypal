namespace PantryPal.Api.Data;

public sealed class ApiKey
{
    public ulong Id { get; set; }

    public string KeyHash { get; set; } = string.Empty;

    public string? Label { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}
