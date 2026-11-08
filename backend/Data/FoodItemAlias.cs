namespace PantryPal.Api.Data;

public sealed class FoodItemAlias
{
    public ulong Id { get; set; }

    public ulong FoodItemId { get; set; }

    public string Alias { get; set; } = string.Empty;

    public FoodItem FoodItem { get; set; } = null!;
}