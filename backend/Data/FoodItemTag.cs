namespace PantryPal.Api.Data;

public sealed class FoodItemTag
{
    public ulong FoodItemId { get; set; }

    public ulong FoodTagId { get; set; }

    public FoodItem FoodItem { get; set; } = null!;

    public FoodTag FoodTag { get; set; } = null!;
}