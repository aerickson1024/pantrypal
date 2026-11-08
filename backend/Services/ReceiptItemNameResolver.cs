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
                .Select(alias => new { alias.Alias, FoodName = alias.FoodItem.Name })
                .ToListAsync(cancellationToken);

            var canonicalNames = matches.ToDictionary(
                match => match.Alias.ToLowerInvariant(),
                match => match.FoodName,
                StringComparer.Ordinal);

            return items
                .Select(item => canonicalNames.TryGetValue(item.ItemName.ToLowerInvariant(), out var canonicalName)
                    ? new ResolvedReceiptItem(item with { ItemName = canonicalName }, true)
                    : new ResolvedReceiptItem(item, false))
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