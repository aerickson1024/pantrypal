using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PantryPal.Api.Services;

namespace PantryPal.Api.Endpoints;

public static class GeminiEndpoints
{
    private const long MaxReceiptImageSize = 10 * 1024 * 1024;
    private static readonly HashSet<string> SupportedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/heic",
        "image/heif"
    };

    public static IEndpointRouteBuilder MapGeminiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/gemini/receipts/parse", ParseReceiptAsync)
            .WithName("ParseReceipt")
            .WithSummary("Extract grocery items from a receipt image")
            .WithDescription("Returns receipt items with quantity, purchase date, and unit cost. Known aliases are replaced with their canonical food item names and marked as matched.")
            .Produces<ReceiptParseResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return endpoints;
    }

    private static async Task<IResult> ParseReceiptAsync(
        HttpRequest request,
        IGeminiService geminiService,
        IReceiptItemNameResolver receiptItemNameResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return TypedResults.Problem(
                title: "A receipt image is required.",
                detail: "Upload an image using the image form field.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var image = form.Files.GetFile("image");
        if (image is null || image.Length == 0)
        {
            return TypedResults.Problem(
                title: "A receipt image is required.",
                detail: "Choose a non-empty image file.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (image.Length > MaxReceiptImageSize)
        {
            return TypedResults.Problem(
                title: "The receipt image is too large.",
                detail: "Choose an image that is 10 MB or smaller.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (!SupportedImageTypes.Contains(image.ContentType))
        {
            return TypedResults.Problem(
                title: "Unsupported image type.",
                detail: "Use a JPEG, PNG, WebP, HEIC, or HEIF image.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        if (!geminiService.IsConfigured)
        {
            return TypedResults.Problem(
                title: "Gemini is not configured.",
                detail: "Set the Gemini:ApiKey user secret for the backend.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var logger = loggerFactory.CreateLogger(nameof(GeminiEndpoints));
        try
        {
            await using var imageStream = image.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await imageStream.CopyToAsync(memoryStream, cancellationToken);
            var extractedItems = await geminiService.ExtractReceiptItemsAsync(
                memoryStream.ToArray(),
                image.ContentType,
                cancellationToken);
            var items = await receiptItemNameResolver.ResolveAliasesAsync(extractedItems, cancellationToken);

            var responseItems = items
                .Select(result => new ReceiptResponseItem(
                    result.Item.ItemName,
                    result.Item.QuantityPurchased,
                    result.Item.DateOfPurchase,
                    result.Item.CostPerItem,
                    result.AliasMatched))
                .ToArray();

            return TypedResults.Ok(new ReceiptParseResponse(responseItems));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FoodItemLookupException exception)
        {
            logger.LogError(exception, "The receipt food item alias lookup failed.");
            return TypedResults.Problem(
                title: "Food item lookup unavailable.",
                detail: "The receipt was parsed, but food item names could not be matched. Try again later.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(exception, "The Gemini receipt request timed out.");
            return TypedResults.Problem(
                title: "Gemini timed out.",
                detail: "Try again in a moment.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException)
        {
            var statusCode = (exception as HttpRequestException)?.StatusCode;
            logger.LogError(exception, "The Gemini receipt request failed with HTTP status {StatusCode}.", statusCode);
            return TypedResults.Problem(
                title: "Receipt parsing failed.",
                detail: "The receipt could not be parsed. Try a clearer image.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

}

/// <summary>Contains the items extracted from a receipt.</summary>
public sealed record ReceiptParseResponse(IReadOnlyList<ReceiptResponseItem> Items);

public sealed record ReceiptResponseItem(
    [property: System.Text.Json.Serialization.JsonPropertyName("item name")] string ItemName,
    [property: System.Text.Json.Serialization.JsonPropertyName("quantity purchased")] decimal QuantityPurchased,
    [property: System.Text.Json.Serialization.JsonPropertyName("date of purchase")] DateOnly DateOfPurchase,
    [property: System.Text.Json.Serialization.JsonPropertyName("cost per item")] decimal CostPerItem,
    [property: System.Text.Json.Serialization.JsonPropertyName("aliasMatched")] bool AliasMatched);