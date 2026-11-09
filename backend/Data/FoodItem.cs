namespace PantryPal.Api.Data;

public sealed class FoodItem
{
    public ulong Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<FoodItemAlias> Aliases { get; set; } = new List<FoodItemAlias>();

    public ICollection<FoodItemTag> Tags { get; set; } = new List<FoodItemTag>();
}