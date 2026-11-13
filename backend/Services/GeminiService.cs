using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PantryPal.Api.Services;

public sealed class GeminiService(HttpClient httpClient) : IGeminiService
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/interactions";
    private const string Model = "gemini-3.5-flash-lite";

    private static readonly object ReceiptResponseFormat = new
    {
        type = "text",
        mime_type = "application/json",
        schema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["items"] = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["item name"] = new { type = "string" },
                            ["quantity purchased"] = new { type = "number" },
                            ["date of purchase"] = new { type = "string", format = "date" },
                            ["cost per item"] = new { type = "number" }
                        },
                        required = new[]
                        {
                            "item name",
                            "quantity purchased",
                            "date of purchase",
                            "cost per item"
                        },
                        additionalProperties = false
                    }
                }
            },
            required = new[] { "items" },
            additionalProperties = false
        }
    };

    public async Task<IReadOnlyList<ReceiptItem>> ExtractReceiptItemsAsync(
        ReadOnlyMemory<byte> imageData,
        string mimeType,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var input = new object[]
        {
            new
            {
                type = "text",
                text = "Read this grocery receipt and extract every purchased product line. " +
                    "Use the receipt purchase date for each item in YYYY-MM-DD format. " +
                    "Quantity must be numeric. Cost per item is the printed unit price; " +
                    "if only a line total is shown for a quantity greater than one, divide the line total by quantity. " +
                    "Do not include tax, subtotal, discounts as separate product items, or payment lines. " +
                    "Do not guess unreadable values; omit a line if a required value cannot be read."
            },
            new
            {
                type = "image",
                data = Convert.ToBase64String(imageData.Span),
                mime_type = mimeType
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Headers.Add("Api-Revision", "2026-05-20");
        request.Content = JsonContent.Create(new
        {
            model = Model,
            input,
            response_format = ReceiptResponseFormat,
            generation_config = new { thinking_level = "minimal" },
            store = false
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException("Gemini returned a non-success status.", null, response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var responseJson = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var root = responseJson.RootElement;

        if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed" ||
            !root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Gemini returned an incomplete response.");
        }

        var textBlocks = new List<string>();
        foreach (var step in steps.EnumerateArray())
        {
            if (!step.TryGetProperty("type", out var stepType) || stepType.GetString() != "model_output" ||
                !step.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var contentType) && contentType.GetString() == "text" &&
                    item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    textBlocks.Add(text.GetString()!);
                }
            }
        }

        var output = string.Join(Environment.NewLine, textBlocks).Trim();
        using var outputJson = JsonDocument.Parse(output);
        if (!outputJson.RootElement.TryGetProperty("items", out var receiptItems) ||
            receiptItems.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Gemini returned an invalid receipt response.");
        }

        var parsedItems = JsonSerializer.Deserialize<List<ReceiptItem>>(
            receiptItems.GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (parsedItems is null || parsedItems.Any(item =>
                string.IsNullOrWhiteSpace(item.ItemName) ||
                item.QuantityPurchased <= 0 ||
                item.DateOfPurchase == default ||
                item.CostPerItem < 0))
        {
            throw new InvalidDataException("Gemini returned invalid receipt item values.");
        }

        return parsedItems;
    }
}