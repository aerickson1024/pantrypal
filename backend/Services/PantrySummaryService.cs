using Microsoft.EntityFrameworkCore;
using PantryPal.Api.Data;
using PantryPal.Api.Endpoints;
using System.Globalization;

namespace PantryPal.Api.Services;

public sealed class PantrySummaryService(PantryPalDbContext dbContext) : IPantrySummaryService
{
    public async Task<PantrySummaryResponse> GetSummaryAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken)
    {
        var purchases = dbContext.Purchases.AsNoTracking();
        if (fromDate is { } startDate)
        {
            purchases = purchases.Where(purchase => purchase.PurchaseDate >= startDate);
        }

        if (toDate is { } endDate)
        {
            purchases = purchases.Where(purchase => purchase.PurchaseDate <= endDate);
        }

        var totals = await purchases
            .GroupBy(purchase => new { purchase.FoodItemId, purchase.FoodItem.Name })
            .OrderBy(group => group.Key.Name)
            .Select(group => new
            {
                group.Key.FoodItemId,
                group.Key.Name,
                TotalQuantity = group.Sum(purchase => purchase.Quantity),
                TotalSpent = group.Sum(purchase => purchase.Quantity * purchase.CostPerItem),
                OldestPurchaseDate = group.Min(purchase => purchase.PurchaseDate),
                NewestPurchaseDate = group.Max(purchase => purchase.PurchaseDate),
            })
            .ToArrayAsync(cancellationToken);

        var foodItemIds = totals.Select(item => item.FoodItemId).ToArray();
        var tagAssignments = await dbContext.FoodItemTags
            .AsNoTracking()
            .Where(link => foodItemIds.Contains(link.FoodItemId))
            .Select(link => new { link.FoodItemId, link.FoodTag.Name })
            .ToArrayAsync(cancellationToken);
        var tagsByFoodItem = tagAssignments
            .GroupBy(link => link.FoodItemId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(link => link.Name)
                    .OrderBy(name => name)
                    .ToArray());
        var items = totals.Select(item => new PantryItemSummaryResponse(
            item.FoodItemId.ToString(CultureInfo.InvariantCulture),
            item.Name,
            item.TotalQuantity,
            item.TotalSpent,
            item.OldestPurchaseDate,
            item.NewestPurchaseDate,
            tagsByFoodItem.GetValueOrDefault(item.FoodItemId, Array.Empty<string>()))).ToArray();

        return new PantrySummaryResponse(items, items.Sum(item => item.TotalSpent));
    }
}