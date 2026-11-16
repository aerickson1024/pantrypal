using PantryPal.Api.Endpoints;

namespace PantryPal.Api.Services;

public interface IFoodTagService
{
    Task<IReadOnlyList<FoodTagSummary>> GetAssignedTagsAsync(ulong userId, CancellationToken cancellationToken);

    Task<FoodItemTagsResponse?> ReplaceFoodItemTagsAsync(
        ulong userId,
        ulong foodItemId,
        IReadOnlyList<string> tagNames,
        CancellationToken cancellationToken);
}