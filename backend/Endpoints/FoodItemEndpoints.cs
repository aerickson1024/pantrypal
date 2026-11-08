using System.Data.Common;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PantryPal.Api.Data;

namespace PantryPal.Api.Endpoints;

public static class FoodItemEndpoints
{
    public static IEndpointRouteBuilder MapFoodItemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/food-items/aliases", SaveAliasAsync)
            .WithName("SaveFoodItemAlias")
            .WithSummary("Save an edited canonical food name and its receipt alias")
            .Produces<FoodItemAliasResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> SaveAliasAsync(
        FoodItemAliasRequest request,
        PantryPalDbContext dbContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var canonicalName = request.CanonicalName?.Trim();
        if (string.IsNullOrWhiteSpace(request.OriginalName) || request.OriginalName.Length > 255 ||
            string.IsNullOrWhiteSpace(canonicalName) || canonicalName.Length > 255)
        {
            return TypedResults.Problem(
                title: "A valid alias and canonical name are required.",
                detail: "Names must contain non-whitespace text and be no longer than 255 characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var logger = loggerFactory.CreateLogger(nameof(FoodItemEndpoints));
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var existingAlias = await dbContext.FoodItemAliases
                .Include(alias => alias.FoodItem)
                .SingleOrDefaultAsync(alias => alias.Alias == request.OriginalName, cancellationToken);

            if (existingAlias is not null)
            {
                if (!string.Equals(existingAlias.FoodItem.Name, canonicalName, StringComparison.OrdinalIgnoreCase))
                {
                    return AliasConflict();
                }

                await transaction.CommitAsync(cancellationToken);
                return TypedResults.Ok(new FoodItemAliasResponse(
                    existingAlias.FoodItem.Name,
                    existingAlias.FoodItemId.ToString(CultureInfo.InvariantCulture)));
            }

            var foodItem = await dbContext.FoodItems
                .SingleOrDefaultAsync(item => item.Name == canonicalName, cancellationToken);
            if (foodItem is null)
            {
                foodItem = new FoodItem { Name = canonicalName };
                dbContext.FoodItems.Add(foodItem);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.FoodItemAliases.Add(new FoodItemAlias
            {
                Alias = request.OriginalName,
                FoodItemId = foodItem.Id
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return TypedResults.Ok(new FoodItemAliasResponse(
                foodItem.Name,
                foodItem.Id.ToString(CultureInfo.InvariantCulture)));
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
                var existingAlias = await dbContext.FoodItemAliases
                    .Include(alias => alias.FoodItem)
                    .SingleOrDefaultAsync(alias => alias.Alias == request.OriginalName, cancellationToken);

                if (existingAlias is not null)
                {
                    if (string.Equals(existingAlias.FoodItem.Name, canonicalName, StringComparison.OrdinalIgnoreCase))
                    {
                        return TypedResults.Ok(new FoodItemAliasResponse(
                            existingAlias.FoodItem.Name,
                            existingAlias.FoodItemId.ToString(CultureInfo.InvariantCulture)));
                    }

                    return AliasConflict();
                }
            }
            catch (Exception lookupException) when (lookupException is DbException or InvalidOperationException)
            {
                logger.LogError(lookupException, "The food item alias could not be checked after a failed save.");
            }

            logger.LogError(exception, "The food item alias save failed.");
            return DatabaseUnavailable();
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            logger.LogError(exception, "The food item alias save failed.");
            return DatabaseUnavailable();
        }
    }

    private static IResult AliasConflict() => TypedResults.Problem(
        title: "This alias is already assigned to another food item.",
        detail: "The existing alias mapping was not changed.",
        statusCode: StatusCodes.Status409Conflict);

    private static IResult DatabaseUnavailable() => TypedResults.Problem(
        title: "Food item lookup unavailable.",
        detail: "The food item could not be saved. Check the database connection and try again.",
        statusCode: StatusCodes.Status503ServiceUnavailable);
}

public sealed record FoodItemAliasRequest(
    [property: JsonPropertyName("originalName")] string OriginalName,
    [property: JsonPropertyName("canonicalName")] string CanonicalName);

public sealed record FoodItemAliasResponse(
    [property: JsonPropertyName("canonicalName")] string CanonicalName,
    [property: JsonPropertyName("foodItemId")] string FoodItemId);