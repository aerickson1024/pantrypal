namespace PantryPal.Api.Services;

public interface IReceiptItemNameResolver
{
    Task<IReadOnlyList<ResolvedReceiptItem>> ResolveAliasesAsync(
        IReadOnlyList<ReceiptItem> items,
        CancellationToken cancellationToken);
}

public sealed record ResolvedReceiptItem(ReceiptItem Item, bool AliasMatched);