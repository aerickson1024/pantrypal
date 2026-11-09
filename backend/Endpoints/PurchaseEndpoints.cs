using System.Data.Common;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PantryPal.Api.Data;
using PantryPal.Api.Services;

namespace PantryPal.Api.Endpoints;

public static class PurchaseEndpoints
{
    private const int MaxLinesPerReceipt = 1000;
    private const decimal MaxQuantity = 999999999999999.999m;
    private const decimal MaxCostPerItem = 9999999999999999.99m;

    public static IEndpointRouteBuilder MapPurchaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/purchases/summary", GetPantrySummaryAsync)
            .WithName("GetPantrySummary")
            .WithSummary("Get aggregate purchase quantities and spending by food item")
            .WithDescription("Returns totals by food item for purchases on or between the inclusive fromDate and toDate values, plus the grand total.")
            .Produces<PantrySummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapPost("/api/purchases", SavePurchasesAsync)
            .WithName("SavePurchases")
            .WithSummary("Save all lines from a parsed receipt")
            .Produces<PurchaseSaveResponse>(StatusCodes.Status200OK)
            .Produces<PurchaseSaveResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> GetPantrySummaryAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        IPantrySummaryService pantrySummaryService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (fromDate is { } startDate && toDate is { } endDate && startDate > endDate)
        {
            return TypedResults.Problem(
                title: "The purchase date range is invalid.",
                detail: "The fromDate must be on or before the toDate.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            return TypedResults.Ok(await pantrySummaryService.GetSummaryAsync(fromDate, toDate, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or InvalidCastException)
        {
            loggerFactory.CreateLogger(nameof(PurchaseEndpoints))
                .LogError(exception, "The pantry purchase summary could not be loaded.");
            return TypedResults.Problem(
                title: "Pantry summary unavailable.",
                detail: "The purchase summary could not be loaded. Check the database connection and try again.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> SavePurchasesAsync(
        PurchaseSaveRequest request,
        PantryPalDbContext dbContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!IsValidHash(request.Hash) || request.Items is null ||
            request.Items.Count == 0 || request.Items.Count > MaxLinesPerReceipt)
        {
            return InvalidRequest();
        }

        var lines = new List<ValidatedPurchaseLine>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (!ulong.TryParse(item.FoodItemId, NumberStyles.None, CultureInfo.InvariantCulture, out var foodItemId) ||
                item.PurchaseDate == default ||
                item.Quantity <= 0 || item.Quantity > MaxQuantity ||
                decimal.Round(item.Quantity, 3) != item.Quantity ||
                item.CostPerItem < 0 || item.CostPerItem > MaxCostPerItem ||
                decimal.Round(item.CostPerItem, 2) != item.CostPerItem)
            {
                return InvalidRequest();
            }

            lines.Add(new ValidatedPurchaseLine(
                foodItemId,
                item.PurchaseDate,
                item.Quantity,
                item.CostPerItem));
        }

        var hash = request.Hash.ToLowerInvariant();
        var logger = loggerFactory.CreateLogger(nameof(PurchaseEndpoints));
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var existingPurchases = await dbContext.Purchases
                .AsNoTracking()
                .Where(purchase => purchase.Hash == hash)
                .OrderBy(purchase => purchase.LineNumber)
                .ToListAsync(cancellationToken);

            if (existingPurchases.Count > 0)
            {
                return HasSameLines(existingPurchases, lines)
                    ? TypedResults.Ok(new PurchaseSaveResponse(hash, lines.Count, true))
                    : PurchaseConflict();
            }

            var foodItemIds = lines.Select(line => line.FoodItemId).Distinct().ToArray();
            var existingFoodItemIds = await dbContext.FoodItems
                .Where(foodItem => foodItemIds.Contains(foodItem.Id))
                .Select(foodItem => foodItem.Id)
                .ToListAsync(cancellationToken);

            if (existingFoodItemIds.Count != foodItemIds.Length)
            {
                return TypedResults.Problem(
                    title: "A receipt item is not linked to a known food.",
                    detail: "Resolve every receipt item to a saved food item, then try again.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                dbContext.Purchases.Add(new Purchase
                {
                    Hash = hash,
                    LineNumber = checked((uint)(index + 1)),
                    FoodItemId = line.FoodItemId,
                    PurchaseDate = line.PurchaseDate,
                    Quantity = line.Quantity,
                    CostPerItem = line.CostPerItem
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Results.Json(
                new PurchaseSaveResponse(hash, lines.Count, false),
                statusCode: StatusCodes.Status201Created);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException exception)
        {
            dbContext.ChangeTracker.Clear();
            try
            {
                var existingPurchases = await dbContext.Purchases
                    .AsNoTracking()
                    .Where(purchase => purchase.Hash == hash)
                    .OrderBy(purchase => purchase.LineNumber)
                    .ToListAsync(cancellationToken);

                if (existingPurchases.Count > 0)
                {
                    return HasSameLines(existingPurchases, lines)
                        ? TypedResults.Ok(new PurchaseSaveResponse(hash, lines.Count, true))
                        : PurchaseConflict();
                }
            }
            catch (Exception lookupException) when (lookupException is DbException or InvalidOperationException or InvalidCastException)
            {
                logger.LogError(lookupException, "The receipt save could not be checked after a failed insert.");
            }

            logger.LogError(exception, "The receipt purchase save failed.");
            return DatabaseUnavailable();
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or InvalidCastException)
        {
            logger.LogError(exception, "The receipt purchase save failed.");
            return DatabaseUnavailable();
        }
    }

    private static bool IsValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    private static bool HasSameLines(
        IReadOnlyList<Purchase> existingPurchases,
        IReadOnlyList<ValidatedPurchaseLine> requestedLines)
    {
        if (existingPurchases.Count != requestedLines.Count)
        {
            return false;
        }

        for (var index = 0; index < requestedLines.Count; index++)
        {
            var existing = existingPurchases[index];
            var requested = requestedLines[index];
            if (existing.LineNumber != (uint)(index + 1) ||
                existing.FoodItemId != requested.FoodItemId ||
                existing.PurchaseDate != requested.PurchaseDate ||
                existing.Quantity != requested.Quantity ||
                existing.CostPerItem != requested.CostPerItem)
            {
                return false;
            }
        }

        return true;
    }

    private static IResult InvalidRequest() => TypedResults.Problem(
        title: "The purchase request is invalid.",
        detail: "Provide a SHA-256 hash and 1 to 1000 valid purchase lines.",
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult PurchaseConflict() => TypedResults.Problem(
        title: "This receipt hash was already saved with different purchase data.",
        detail: "The existing purchases were not changed.",
        statusCode: StatusCodes.Status409Conflict);

    private static IResult DatabaseUnavailable() => TypedResults.Problem(
        title: "Purchase storage unavailable.",
        detail: "The purchases could not be saved. Check the database connection and try again.",
        statusCode: StatusCodes.Status503ServiceUnavailable);

    private sealed record ValidatedPurchaseLine(
        ulong FoodItemId,
        DateOnly PurchaseDate,
        decimal Quantity,
        decimal CostPerItem);
}

public sealed record PurchaseSaveRequest(
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("items")] IReadOnlyList<PurchaseLineRequest> Items);

public sealed record PurchaseLineRequest(
    [property: JsonPropertyName("foodItemId")] string FoodItemId,
    [property: JsonPropertyName("dateOfPurchase")] DateOnly PurchaseDate,
    [property: JsonPropertyName("quantityPurchased")] decimal Quantity,
    [property: JsonPropertyName("costPerItem")] decimal CostPerItem);

public sealed record PurchaseSaveResponse(
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("itemsSaved")] int ItemsSaved,
    [property: JsonPropertyName("alreadySaved")] bool AlreadySaved);