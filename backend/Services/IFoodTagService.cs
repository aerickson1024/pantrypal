using PantryPal.Api.Endpoints;

namespace PantryPal.Api.Services;

public interface IFoodTagService
{
    Task<IReadOnlyList<FoodTagSummary>> GetAssignedTagsAsync(CancellationToken cancellationToken);

    Task<FoodItemTagsResponse?> ReplaceFoodItemTagsAsync(
        ulong foodItemId,
        IReadOnlyList<string> tagNames,
        CancellationToken cancellationToken);
}