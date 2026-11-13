namespace PantryPal.Api.Services;

public interface IGeminiService
{
    Task<IReadOnlyList<ReceiptItem>> ExtractReceiptItemsAsync(
        ReadOnlyMemory<byte> imageData,
        string mimeType,
        string apiKey,
        CancellationToken cancellationToken);
}

public sealed record ReceiptItem(
    [property: System.Text.Json.Serialization.JsonPropertyName("item name")] string ItemName,
    [property: System.Text.Json.Serialization.JsonPropertyName("quantity purchased")] decimal QuantityPurchased,
    [property: System.Text.Json.Serialization.JsonPropertyName("date of purchase")] DateOnly DateOfPurchase,
    [property: System.Text.Json.Serialization.JsonPropertyName("cost per item")] decimal CostPerItem);