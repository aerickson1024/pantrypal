namespace PantryPal.Api.Data;

public sealed class FoodTag
{
    public ulong Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<FoodItemTag> FoodItems { get; set; } = new List<FoodItemTag>();
}