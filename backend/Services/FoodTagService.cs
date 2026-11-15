using Microsoft.EntityFrameworkCore;
using PantryPal.Api.Data;
using PantryPal.Api.Endpoints;

namespace PantryPal.Api.Services;

public sealed class FoodTagService(PantryPalDbContext dbContext) : IFoodTagService
{
    public async Task<IReadOnlyList<FoodTagSummary>> GetAssignedTagsAsync(ulong userId, CancellationToken cancellationToken) =>
        await dbContext.FoodTags
            .AsNoTracking()
            .Where(tag => tag.UserId == userId && tag.FoodItems.Any())
            .OrderByDescending(tag => tag.FoodItems.Count())
            .ThenBy(tag => tag.Name)
            .Select(tag => new FoodTagSummary(tag.Name, tag.FoodItems.Count()))
            .ToArrayAsync(cancellationToken);

    public async Task<FoodItemTagsResponse?> ReplaceFoodItemTagsAsync(
        ulong userId,
        ulong foodItemId,
        IReadOnlyList<string> tagNames,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await dbContext.FoodItems.AnyAsync(item => item.Id == foodItemId && item.UserId == userId, cancellationToken))
        {
            return null;
        }

        var tags = await dbContext.FoodTags
            .Where(tag => tag.UserId == userId && tagNames.Contains(tag.Name))
            .ToListAsync(cancellationToken);
        var tagsByName = tags.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var tagName in tagNames)
        {
            if (tagsByName.ContainsKey(tagName))
            {
                continue;
            }

            var tag = new FoodTag { UserId = userId, Name = tagName };
            dbContext.FoodTags.Add(tag);
            tags.Add(tag);
            tagsByName.Add(tagName, tag);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var requestedTagIds = tags.Select(tag => tag.Id).ToHashSet();
        var assignments = await dbContext.FoodItemTags
            .Where(link => link.FoodItemId == foodItemId)
            .ToListAsync(cancellationToken);
        var assignedTagIds = assignments.Select(link => link.FoodTagId).ToHashSet();

        dbContext.FoodItemTags.RemoveRange(assignments.Where(link => !requestedTagIds.Contains(link.FoodTagId)));
        foreach (var tag in tags.Where(tag => !assignedTagIds.Contains(tag.Id)))
        {
            dbContext.FoodItemTags.Add(new FoodItemTag
            {
                FoodItemId = foodItemId,
                FoodTagId = tag.Id,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var unusedTags = await dbContext.FoodTags
            .Where(tag => tag.UserId == userId && !tag.FoodItems.Any())
            .ToArrayAsync(cancellationToken);
        dbContext.FoodTags.RemoveRange(unusedTags);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var availableTags = await GetAssignedTagsAsync(userId, cancellationToken);

        return new FoodItemTagsResponse(
            foodItemId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            tags.OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
                .Select(tag => tag.Name)
                .ToArray(),
            availableTags);
    }
}