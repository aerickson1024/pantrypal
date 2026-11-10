using System.Text.Json.Serialization;

namespace PantryPal.Api.Endpoints;

/// <summary>Aggregated purchase totals for each food item and the whole pantry.</summary>
public sealed record PantrySummaryResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<PantryItemSummaryResponse> Items,
    [property: JsonPropertyName("grandTotal")] decimal GrandTotal);

/// <summary>Aggregated quantity and spending for one food item.</summary>
public sealed record PantryItemSummaryResponse(
    [property: JsonPropertyName("foodItemId")] string FoodItemId,
    [property: JsonPropertyName("itemName")] string ItemName,
    [property: JsonPropertyName("totalQuantity")] decimal TotalQuantity,
    [property: JsonPropertyName("totalSpent")] decimal TotalSpent,
    [property: JsonPropertyName("oldestPurchaseDate")] DateOnly OldestPurchaseDate,
    [property: JsonPropertyName("newestPurchaseDate")] DateOnly NewestPurchaseDate,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags);