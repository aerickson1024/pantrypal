using System.Net.Http;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PantryPal.Api.Services;

namespace PantryPal.Api.Endpoints;

public static class GeminiEndpoints
{
    public const long MaxReceiptImageSize = 10 * 1024 * 1024;
    private static readonly HashSet<string> SupportedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/heic",
        "image/heif"
    };
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static IEndpointRouteBuilder MapGeminiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/gemini/receipts/parse", ParseReceiptAsync)
            .WithName("ParseReceipt")
            .WithSummary("Extract grocery items from a receipt image")
            .WithDescription("Returns a SHA-256 hash of normalized visible receipt text and receipt items with quantity, purchase date, unit cost, alias-match status, and canonical food item IDs when available.")
            .Produces<ReceiptParseResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return endpoints;
    }

    // Identifies the image from its leading bytes, since the client-declared content type is untrusted.
    private static string? DetectImageType(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.AsSpan().StartsWith(PngSignature))
        {
            return "image/png";
        }

        if (bytes.Length >= 12 &&
            bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
            bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (bytes.Length >= 12 && bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8))
        {
            var brand = bytes.AsSpan(8, 4);
            if (brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8))
            {
                return "image/heic";
            }

            if (brand.SequenceEqual("mif1"u8) || brand.SequenceEqual("msf1"u8) || brand.SequenceEqual("heif"u8))
            {
                return "image/heif";
            }
        }

        return null;
    }

    private static async Task<IResult> ParseReceiptAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        IAccountService accountService,
        IGeminiService geminiService,
        IReceiptItemNameResolver receiptItemNameResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (user.GetUserId() is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        if (!request.HasFormContentType)
        {
            return TypedResults.Problem(
                title: "A receipt image is required.",
                detail: "Upload an image using the image form field.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return TypedResults.Problem(
                title: "The receipt image is too large.",
                detail: "Choose an image that is 10 MB or smaller.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

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

        var geminiApiKey = await accountService.GetGeminiApiKeyAsync(userId, cancellationToken);
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            return TypedResults.Problem(
                title: "A Gemini API key is required.",
                detail: "Save your Gemini API key on the Account page, then try again.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var logger = loggerFactory.CreateLogger(nameof(GeminiEndpoints));
        try
        {
            await using var imageStream = image.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await imageStream.CopyToAsync(memoryStream, cancellationToken);
            var imageBytes = memoryStream.ToArray();
            var imageType = DetectImageType(imageBytes);
            if (imageType is null)
            {
                return TypedResults.Problem(
                    title: "Unsupported image type.",
                    detail: "Use a JPEG, PNG, WebP, HEIC, or HEIF image.",
                    statusCode: StatusCodes.Status415UnsupportedMediaType);
            }

            var hash = ReceiptHash.Compute(imageBytes);
            var extractedItems = await geminiService.ExtractReceiptItemsAsync(
                imageBytes,
                imageType,
                geminiApiKey,
                cancellationToken);
            var items = await receiptItemNameResolver.ResolveAliasesAsync(userId, extractedItems, cancellationToken);

            var responseItems = items
                .Select(result => new ReceiptResponseItem(
                    result.Item.ItemName,
                    decimal.Round(result.Item.QuantityPurchased, 3, MidpointRounding.AwayFromZero),
                    result.Item.DateOfPurchase,
                    decimal.Round(result.Item.CostPerItem, 2, MidpointRounding.AwayFromZero),
                    result.AliasMatched,
                    result.FoodItemId?.ToString(CultureInfo.InvariantCulture)))
                .ToArray();

            return TypedResults.Ok(new ReceiptParseResponse(responseItems, hash));
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
public sealed record ReceiptParseResponse(
    IReadOnlyList<ReceiptResponseItem> Items,
    [property: System.Text.Json.Serialization.JsonPropertyName("hash")] string Hash);

public sealed record ReceiptResponseItem(
    [property: System.Text.Json.Serialization.JsonPropertyName("item name")] string ItemName,
    [property: System.Text.Json.Serialization.JsonPropertyName("quantity purchased")] decimal QuantityPurchased,
    [property: System.Text.Json.Serialization.JsonPropertyName("date of purchase")] DateOnly DateOfPurchase,
    [property: System.Text.Json.Serialization.JsonPropertyName("cost per item")] decimal CostPerItem,
    [property: System.Text.Json.Serialization.JsonPropertyName("aliasMatched")] bool AliasMatched,
    [property: System.Text.Json.Serialization.JsonPropertyName("foodItemId")] string? FoodItemId);