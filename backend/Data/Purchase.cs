namespace PantryPal.Api.Data;

public sealed class Purchase
{
    public ulong Id { get; set; }

    public string Hash { get; set; } = string.Empty;

    public uint LineNumber { get; set; }

    public ulong FoodItemId { get; set; }

    public DateOnly PurchaseDate { get; set; }

    public decimal Quantity { get; set; }

    public decimal CostPerItem { get; set; }

    public DateTime CreatedAt { get; set; }

    public FoodItem FoodItem { get; set; } = null!;
}