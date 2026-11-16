namespace PantryPal.Api.Data;

public sealed class InviteCode
{
    public ulong Id { get; set; }

    public string CodeHash { get; set; } = string.Empty;

    public ulong? UsedByUserId { get; set; }

    public DateTime? UsedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
