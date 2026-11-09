using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PantryPal.Api.Data;

namespace PantryPal.Api.Services;

public sealed class FoodItemSearchService(PantryPalDbContext dbContext)
{
    private const int MaximumResults = 8;
    private const double MinimumSimilarity = 0.7;

    public async Task<IReadOnlyList<FoodItemSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0)
        {
            return Array.Empty<FoodItemSearchResult>();
        }

        var foodItems = await dbContext.FoodItems
            .AsNoTracking()
            .Select(foodItem => new { foodItem.Id, foodItem.Name })
            .ToListAsync(cancellationToken);

        var rankedItems = foodItems
            .Select(foodItem => new
            {
                foodItem.Id,
                foodItem.Name,
                NormalizedName = Normalize(foodItem.Name)
            })
            .Select(foodItem => new
            {
                foodItem.Id,
                foodItem.Name,
                foodItem.NormalizedName,
                IsPrefixMatch = foodItem.NormalizedName.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase),
                Similarity = CalculateSimilarity(normalizedQuery, foodItem.NormalizedName)
            })
            .ToArray();

        var prefixMatches = rankedItems
            .Where(foodItem => foodItem.IsPrefixMatch)
            .OrderByDescending(foodItem => foodItem.Name.Length)
            .ThenBy(foodItem => foodItem.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(foodItem => foodItem.Id);
        var fuzzyMatches = rankedItems
            .Where(foodItem => !foodItem.IsPrefixMatch && foodItem.Similarity >= MinimumSimilarity)
            .OrderByDescending(foodItem => foodItem.Similarity)
            .ThenBy(foodItem => foodItem.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(foodItem => foodItem.Id)
            .Take(MaximumResults);

        return prefixMatches
            .Concat(fuzzyMatches)
            .Select(foodItem => new FoodItemSearchResult(
                foodItem.Name,
                foodItem.Id.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
    }

    private static string Normalize(string value) => string.Join(
        ' ',
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static double CalculateSimilarity(string query, string candidate)
    {
        var longestLength = Math.Max(query.Length, candidate.Length);
        if (longestLength == 0)
        {
            return 1;
        }

        var editSimilarity = 1 - (double)DamerauLevenshteinDistance(query, candidate) / longestLength;
        var queryTokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateTokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sharedTokens = queryTokens.Intersect(candidateTokens, StringComparer.OrdinalIgnoreCase).Count();
        var tokenSimilarity = queryTokens.Count + candidateTokens.Count == 0
            ? 0
            : 2d * sharedTokens / (queryTokens.Count + candidateTokens.Count);
        var substringSimilarity = query.Contains(candidate, StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains(query, StringComparison.OrdinalIgnoreCase)
                ? (double)Math.Min(query.Length, candidate.Length) / longestLength
                : 0;

        return Math.Max(editSimilarity, Math.Max(tokenSimilarity * 0.9, substringSimilarity * 0.95));
    }

    private static int DamerauLevenshteinDistance(string left, string right)
    {
        var distances = new int[left.Length + 1, right.Length + 1];
        for (var leftIndex = 0; leftIndex <= left.Length; leftIndex++)
        {
            distances[leftIndex, 0] = leftIndex;
        }

        for (var rightIndex = 0; rightIndex <= right.Length; rightIndex++)
        {
            distances[0, rightIndex] = rightIndex;
        }

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitutionCost = CharactersMatch(left[leftIndex - 1], right[rightIndex - 1]) ? 0 : 1;
                distances[leftIndex, rightIndex] = Math.Min(
                    Math.Min(
                        distances[leftIndex - 1, rightIndex] + 1,
                        distances[leftIndex, rightIndex - 1] + 1),
                    distances[leftIndex - 1, rightIndex - 1] + substitutionCost);

                if (leftIndex > 1 && rightIndex > 1 &&
                    CharactersMatch(left[leftIndex - 1], right[rightIndex - 2]) &&
                    CharactersMatch(left[leftIndex - 2], right[rightIndex - 1]))
                {
                    distances[leftIndex, rightIndex] = Math.Min(
                        distances[leftIndex, rightIndex],
                        distances[leftIndex - 2, rightIndex - 2] + substitutionCost);
                }
            }
        }

        return distances[left.Length, right.Length];
    }

    private static bool CharactersMatch(char left, char right) =>
        char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
}

public sealed record FoodItemSearchResult(string CanonicalName, string FoodItemId);