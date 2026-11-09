using System.Data.Common;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PantryPal.Api.Services;

namespace PantryPal.Api.Endpoints;

public static class FoodTagEndpoints
{
    private const int MaxTagNameLength = 100;

    public static IEndpointRouteBuilder MapFoodTagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tags", GetTagsAsync)
            .WithName("GetFoodTags")
            .WithSummary("List tags assigned to food items")
            .Produces<IReadOnlyList<FoodTagSummary>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapPut("/api/food-items/{foodItemId}/tags", ReplaceFoodItemTagsAsync)
            .WithName("ReplaceFoodItemTags")
            .WithSummary("Replace all tags assigned to a food item")
            .Produces<FoodItemTagsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> GetTagsAsync(
        IFoodTagService foodTagService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await foodTagService.GetAssignedTagsAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or InvalidCastException)
        {
            loggerFactory.CreateLogger(nameof(FoodTagEndpoints)).LogError(exception, "The food tags could not be loaded.");
            return TypedResults.Problem(
                title: "Food tags unavailable.",
                detail: "Food tags could not be loaded. Check the database connection and try again.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> ReplaceFoodItemTagsAsync(
        ulong foodItemId,
        UpdateFoodItemTagsRequest request,
        IFoodTagService foodTagService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (request.Tags is null || request.Tags.Any(tag =>
                string.IsNullOrWhiteSpace(tag) || tag.Trim().Length > MaxTagNameLength))
        {
            return TypedResults.Problem(
                title: "The food item tags are invalid.",
                detail: $"Tags must contain non-whitespace text and be no longer than {MaxTagNameLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var tagNames = request.Tags
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        try
        {
            var result = await foodTagService.ReplaceFoodItemTagsAsync(foodItemId, tagNames, cancellationToken);
            return result is null
                ? TypedResults.Problem(
                    title: "Food item not found.",
                    detail: "The requested food item does not exist.",
                    statusCode: StatusCodes.Status404NotFound)
                : TypedResults.Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException exception)
        {
            loggerFactory.CreateLogger(nameof(FoodTagEndpoints)).LogWarning(exception, "Food tag changes conflicted.");
            return TypedResults.Problem(
                title: "The food tag changes conflicted.",
                detail: "The tags changed while this update was being saved. Refresh the pantry items and try again.",
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or InvalidCastException)
        {
            loggerFactory.CreateLogger(nameof(FoodTagEndpoints)).LogError(exception, "The food item tags could not be saved.");
            return TypedResults.Problem(
                title: "Food tags unavailable.",
                detail: "The food item tags could not be saved. Check the database connection and try again.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}

/// <summary>Request to replace all tags assigned to a food item.</summary>
public sealed record UpdateFoodItemTagsRequest(
    [property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags);

/// <summary>The current tags assigned to a food item.</summary>
public sealed record FoodItemTagsResponse(
    [property: JsonPropertyName("foodItemId")] string FoodItemId,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("availableTags")] IReadOnlyList<FoodTagSummary> AvailableTags);

/// <summary>A tag and the number of food items it is assigned to.</summary>
public sealed record FoodTagSummary(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("usageCount")] int UsageCount);