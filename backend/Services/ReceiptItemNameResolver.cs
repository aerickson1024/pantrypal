using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PantryPal.Api.Data;

namespace PantryPal.Api.Services;

public sealed class ReceiptItemNameResolver(PantryPalDbContext dbContext) : IReceiptItemNameResolver
{
    public async Task<IReadOnlyList<ResolvedReceiptItem>> ResolveAliasesAsync(
        IReadOnlyList<ReceiptItem> items,
        CancellationToken cancellationToken)
    {
        var normalizedNames = items
            .Select(item => item.ItemName.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedNames.Length == 0)
        {
            return Array.Empty<ResolvedReceiptItem>();
        }

        try
        {
            var matches = await dbContext.FoodItemAliases
                .Where(alias => normalizedNames.Contains(alias.Alias))
                .Select(alias => new { alias.Alias, alias.FoodItemId, FoodName = alias.FoodItem.Name })
                .ToListAsync(cancellationToken);

            var canonicalItems = matches.ToDictionary(
                match => match.Alias.ToLowerInvariant(),
                match => (match.FoodItemId, match.FoodName),
                StringComparer.Ordinal);

            return items
                .Select(item => canonicalItems.TryGetValue(item.ItemName.ToLowerInvariant(), out var canonicalItem)
                    ? new ResolvedReceiptItem(item with { ItemName = canonicalItem.FoodName }, true, canonicalItem.FoodItemId)
                    : new ResolvedReceiptItem(item, false, null))
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            throw new FoodItemLookupException(exception);
        }
    }
}

public sealed class FoodItemLookupException(Exception innerException)
    : Exception("The food item alias lookup failed.", innerException);